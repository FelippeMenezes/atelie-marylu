using AtelieMarylu.Domain.Auditing;
using AtelieMarylu.Domain.Carts;
using AtelieMarylu.Domain.Shipping;
using AtelieMarylu.Domain.Payments;
using AtelieMarylu.Domain.Orders;
using AtelieMarylu.Web.Configuration;
using AtelieMarylu.Infrastructure.Integrations;
using AtelieMarylu.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AtelieMarylu.Tests.Integration;

// Relógio controlável: permite testar a expiração sem esperar horas
public class FakeClock : TimeProvider
{
    // Sem frações de segundo: o Postgres guarda microssegundos e a comparação exata falharia
    public DateTimeOffset Now { get; set; } = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    public override DateTimeOffset GetUtcNow() => Now;
}

public class OrderServiceTests(TestDatabase database) : IClassFixture<TestDatabase>
{
    private readonly FakeClock clock = new();
    private readonly ShopOptions store = new() { PixDiscountPercent = 5, PaymentDeadlineMinutes = 120, MaxPendingOrdersPerCustomer = 3 };

    private OrderService Service()
    {
        var audit = new AuditService(new CurrentUserService(new ServiceCollection().BuildServiceProvider(), new HttpContextAccessor()), database);
        return new OrderService(database, new SimulatedShippingCalculator(), new SimulatedPaymentGateway(), audit, new OrderEvents(),
            Options.Create(store), clock, NullLogger<OrderService>.Instance);
    }

    private static ShippingAddress Address() => new()
    {
        PostalCode = "20000-000", Street = "Rua A", Number = "1", District = "Centro", City = "Rio de Janeiro", State = "RJ",
    };

    [FactWithDatabase]
    public async Task Creating_order_uses_database_price_reduces_stock_and_audits()
    {
        var customer = await database.CreateCustomerAsync();
        var product = await database.CreateProductAsync(stock: 5, price: 89.90m);

        var order = await Service().CreateAsync(customer.Id, [new CartItem(product.Id, 2)], Address(), "pac");

        Assert.Equal(179.80m, order.Subtotal);
        Assert.Equal("20000000", order.Address.PostalCode);
        Assert.Equal(5m, order.PixDiscountPercent);
        Assert.Equal(clock.Now.UtcDateTime.AddMinutes(120), order.PayBy);
        Assert.Equal(3, await database.GetStockAsync(product.Id));
        await using var db = database.CreateDbContext();
        Assert.True(await db.AuditEntries.AnyAsync(a => a.Action == AuditActions.OrderCreated && a.EntityId == order.Id.ToString()));
    }

    [FactWithDatabase]
    public async Task Does_not_sell_more_than_stock()
    {
        var customer = await database.CreateCustomerAsync();
        var product = await database.CreateProductAsync(stock: 1);

        await Assert.ThrowsAsync<OrderRuleException>(() =>
            Service().CreateAsync(customer.Id, [new CartItem(product.Id, 2)], Address(), "pac"));
        Assert.Equal(1, await database.GetStockAsync(product.Id));
    }

    [FactWithDatabase]
    public async Task Two_simultaneous_purchases_of_last_item_only_one_succeeds()
    {
        var product = await database.CreateProductAsync(stock: 1);
        var customers = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => database.CreateCustomerAsync()));

        var attempts = customers.Select(c => Task.Run(async () =>
        {
            try
            {
                await Service().CreateAsync(c.Id, [new CartItem(product.Id, 1)], Address(), "pac");
                return true;
            }
            catch (OrderRuleException)
            {
                return false;
            }
        }));
        var results = await Task.WhenAll(attempts);

        Assert.Equal(1, results.Count(ok => ok));
        Assert.Equal(0, await database.GetStockAsync(product.Id));
    }

    [FactWithDatabase]
    public async Task Limits_pending_orders_per_customer()
    {
        var customer = await database.CreateCustomerAsync();
        var product = await database.CreateProductAsync(stock: 10);
        var service = Service();
        for (var i = 0; i < 3; i++)
            await service.CreateAsync(customer.Id, [new CartItem(product.Id, 1)], Address(), "pac");

        await Assert.ThrowsAsync<OrderRuleException>(() =>
            service.CreateAsync(customer.Id, [new CartItem(product.Id, 1)], Address(), "pac"));

        // Pedido lançado pelo painel não entra no limite
        await service.CreateAsync(customer.Id, [new CartItem(product.Id, 1)], Address(), "pac", createdBy: "admin");
    }

    [FactWithDatabase]
    public async Task Invalid_shipping_is_rejected()
    {
        var customer = await database.CreateCustomerAsync();
        var product = await database.CreateProductAsync(stock: 3);

        await Assert.ThrowsAsync<OrderRuleException>(() =>
            Service().CreateAsync(customer.Id, [new CartItem(product.Id, 1)], Address(), "frete-inventado"));
    }

    [FactWithDatabase]
    public async Task Approved_webhook_marks_order_paid_with_pix_discount()
    {
        var customer = await database.CreateCustomerAsync();
        var product = await database.CreateProductAsync(stock: 3, price: 100m);
        var service = Service();
        var order = await service.CreateAsync(customer.Id, [new CartItem(product.Id, 1)], Address(), "sedex");

        var payment = await service.StartPaymentAsync(order.Id, PaymentMethod.Pix);
        Assert.Equal(order.Total - 5m, payment.Amount);
        Assert.NotNull(payment.PixCopyPaste);

        var found = await service.ProcessNotificationAsync(new PaymentNotification("evt-1", payment.ExternalId!, PaymentStatus.Approved, payment.Amount));

        Assert.True(found);
        await using var db = database.CreateDbContext();
        var stored = await db.Orders.Include(p => p.Payments).SingleAsync(p => p.Id == order.Id);
        Assert.Equal(OrderStatus.Paid, stored.Status);
        Assert.Equal(PaymentStatus.Approved, stored.Payments.Single().Status);
    }

    [FactWithDatabase]
    public async Task Repeated_webhook_is_processed_once()
    {
        var customer = await database.CreateCustomerAsync();
        var product = await database.CreateProductAsync(stock: 3);
        var service = Service();
        var order = await service.CreateAsync(customer.Id, [new CartItem(product.Id, 1)], Address(), "pac");
        var payment = await service.StartPaymentAsync(order.Id, PaymentMethod.Pix);
        var notification = new PaymentNotification("evt-repetido", payment.ExternalId!, PaymentStatus.Approved);

        await service.ProcessNotificationAsync(notification);
        await service.ProcessNotificationAsync(notification);
        await service.ProcessNotificationAsync(notification);

        await using var db = database.CreateDbContext();
        var entries = await db.AuditEntries.CountAsync(a => a.Action == AuditActions.PaymentNotified && a.EntityId == order.Id.ToString());
        Assert.Equal(1, entries);
    }

    [FactWithDatabase]
    public async Task Payment_of_old_amount_raises_alert_and_does_not_confirm()
    {
        var customer = await database.CreateCustomerAsync();
        var a = await database.CreateProductAsync(stock: 5);
        var b = await database.CreateProductAsync(stock: 5);
        var service = Service();
        var order = await service.CreateAsync(customer.Id, [new CartItem(a.Id, 1)], Address(), "pac", createdBy: "admin");
        var oldPix = await service.StartPaymentAsync(order.Id, PaymentMethod.Pix);

        await service.AddItemAsync(order.Id, b.Id, 1);
        await service.ProcessNotificationAsync(new PaymentNotification("evt-antigo", oldPix.ExternalId!, PaymentStatus.Approved, oldPix.Amount));

        await using var db = database.CreateDbContext();
        var stored = await db.Orders.SingleAsync(p => p.Id == order.Id);
        Assert.Equal(OrderStatus.AwaitingPayment, stored.Status);
        Assert.True(await db.AuditEntries.AnyAsync(x => x.Action == AuditActions.PaymentMismatch && x.EntityId == order.Id.ToString()));
    }

    [FactWithDatabase]
    public async Task Webhook_of_unknown_payment_returns_false()
    {
        Assert.False(await Service().ProcessNotificationAsync(new PaymentNotification("x", "nao-existe", PaymentStatus.Approved)));
    }

    [FactWithDatabase]
    public async Task Cancel_restores_stock()
    {
        var customer = await database.CreateCustomerAsync();
        var product = await database.CreateProductAsync(stock: 4);
        var service = Service();
        var order = await service.CreateAsync(customer.Id, [new CartItem(product.Id, 3)], Address(), "pac");

        await service.CancelAsync(order.Id, "teste");

        Assert.Equal(4, await database.GetStockAsync(product.Id));
    }

    [FactWithDatabase]
    public async Task Overdue_orders_are_cancelled_and_stock_returns()
    {
        var customer = await database.CreateCustomerAsync();
        var product = await database.CreateProductAsync(stock: 4);
        var service = Service();
        var overdue = await service.CreateAsync(customer.Id, [new CartItem(product.Id, 2)], Address(), "pac");
        var paid = await service.CreateAsync(customer.Id, [new CartItem(product.Id, 1)], Address(), "pac");
        await service.RecordManualPaymentAsync(paid.Id, PaymentMethod.Pix);

        clock.Now = clock.Now.AddHours(3);
        await service.ExpireOverdueAsync();

        await using var db = database.CreateDbContext();
        Assert.Equal(OrderStatus.Cancelled, (await db.Orders.SingleAsync(p => p.Id == overdue.Id)).Status);
        Assert.Equal(OrderStatus.Paid, (await db.Orders.SingleAsync(p => p.Id == paid.Id)).Status);
        Assert.Equal(3, await database.GetStockAsync(product.Id));
    }

    [FactWithDatabase]
    public async Task Simultaneous_edit_of_same_order_is_detected()
    {
        var customer = await database.CreateCustomerAsync();
        var product = await database.CreateProductAsync(stock: 4);
        var order = await Service().CreateAsync(customer.Id, [new CartItem(product.Id, 1)], Address(), "pac");

        await using var db1 = database.CreateDbContext();
        await using var db2 = database.CreateDbContext();
        var copy1 = await db1.Orders.SingleAsync(p => p.Id == order.Id);
        var copy2 = await db2.Orders.SingleAsync(p => p.Id == order.Id);

        copy1.Notes = "primeira";
        await db1.SaveChangesAsync();
        copy2.Notes = "segunda";

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db2.SaveChangesAsync());
    }

    [FactWithDatabase]
    public async Task Editing_quantity_adjusts_stock_and_removes_item()
    {
        var customer = await database.CreateCustomerAsync();
        var a = await database.CreateProductAsync(stock: 10);
        var b = await database.CreateProductAsync(stock: 10);
        var service = Service();
        var order = await service.CreateAsync(customer.Id, [new CartItem(a.Id, 2), new CartItem(b.Id, 1)], Address(), "pac");

        await service.ChangeQuantityAsync(order.Id, a.Id, 5);
        await service.ChangeQuantityAsync(order.Id, b.Id, 0);

        Assert.Equal(5, await database.GetStockAsync(a.Id));
        Assert.Equal(10, await database.GetStockAsync(b.Id));
        await using var db = database.CreateDbContext();
        var stored = await db.Orders.Include(p => p.Items).SingleAsync(p => p.Id == order.Id);
        Assert.Single(stored.Items);
    }

    [FactWithDatabase]
    public async Task Manual_payment_confirms_order()
    {
        var customer = await database.CreateCustomerAsync();
        var product = await database.CreateProductAsync(stock: 2);
        var service = Service();
        var order = await service.CreateAsync(customer.Id, [new CartItem(product.Id, 1)], Address(), SimulatedShippingCalculator.PickupCode, createdBy: "admin");

        await service.RecordManualPaymentAsync(order.Id, PaymentMethod.Pix);

        await using var db = database.CreateDbContext();
        var stored = await db.Orders.Include(p => p.Payments).SingleAsync(p => p.Id == order.Id);
        Assert.Equal(OrderStatus.Paid, stored.Status);
        Assert.Equal("manual", stored.Payments.Single().Provider);
        Assert.Equal("admin", stored.CreatedBy);
        Assert.Equal(clock.Now.UtcDateTime.AddHours(store.AdminPaymentDeadlineHours), stored.PayBy);
    }
}
