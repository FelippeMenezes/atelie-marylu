using AtelieMarylu.Domain.Auditing;
using AtelieMarylu.Domain.Carts;
using AtelieMarylu.Domain.Orders;
using AtelieMarylu.Domain.Payments;
using AtelieMarylu.Domain.Shipping;
using AtelieMarylu.Infrastructure.Integrations;
using AtelieMarylu.Web;
using AtelieMarylu.Web.Configuration;
using AtelieMarylu.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AtelieMarylu.Tests.Integration;

// Cancelamento pedido pela cliente em "Meus pedidos" (AML-0040)
public class CancellationRequestTests(TestDatabase database) : IClassFixture<TestDatabase>
{
    private readonly FakeClock clock = new();
    private readonly ShopOptions store = new() { PaymentDeadlineMinutes = 120, MaxPendingOrdersPerCustomer = 3 };

    private OrderService Service()
    {
        var audit = new AuditService(new CurrentUserService(new ServiceCollection().BuildServiceProvider(), new HttpContextAccessor()), database);
        var gateway = new SimulatedPaymentGateway();
        var localizer = new ServiceCollection().AddLogging().AddLocalization(o => o.ResourcesPath = "Resources").BuildServiceProvider()
            .GetRequiredService<IStringLocalizer<SharedResource>>();
        return new OrderService(database, new SimulatedShippingCalculator(), gateway, new InlineChargeCloser(gateway), audit, new OrderEvents(),
            Options.Create(store), clock, localizer, NullLogger<OrderService>.Instance);
    }

    private static ShippingAddress Address() => new()
    {
        PostalCode = "20000-000", Street = "Rua A", Number = "1", District = "Centro", City = "Rio de Janeiro", State = "RJ",
    };

    private async Task<(string CustomerId, int ProductId, Order Order)> CreateOrderAsync(OrderService service, bool paid)
    {
        var customer = await database.CreateCustomerAsync();
        var product = await database.CreateProductAsync(stock: 5);
        var order = await service.CreateAsync(customer.Id, [new CartItem(product.Id, 2)], Address(), SimulatedShippingCalculator.PickupCode);
        if (paid) await service.RecordManualPaymentAsync(order.Id, PaymentMethod.Pix);
        return (customer.Id, product.Id, order);
    }

    [FactWithDatabase]
    public async Task Unpaid_order_is_cancelled_now_and_stock_returns()
    {
        var service = Service();
        var (customerId, productId, order) = await CreateOrderAsync(service, paid: false);

        var outcome = await service.RequestCancellationAsync(order.Code, customerId, "  mudei de ideia  ");

        Assert.True(outcome.CancelledNow);
        Assert.Null(outcome.Protocol);
        Assert.Equal(5, await database.GetStockAsync(productId));
        await using var db = database.CreateDbContext();
        var stored = await db.Orders.Include(o => o.CancellationRequests).SingleAsync(o => o.Id == order.Id);
        Assert.Equal(OrderStatus.Cancelled, stored.Status);
        Assert.Equal("Cancelado pela cliente: mudei de ideia", stored.CancellationReason);
        Assert.Empty(stored.CancellationRequests);
    }

    [FactWithDatabase]
    public async Task Paid_order_only_registers_the_request_and_audits_without_the_reason()
    {
        var service = Service();
        var (customerId, productId, order) = await CreateOrderAsync(service, paid: true);

        var outcome = await service.RequestCancellationAsync(order.Code, customerId, "Comprei o tamanho errado");

        Assert.False(outcome.CancelledNow);
        Assert.StartsWith("CAN-", outcome.Protocol);
        Assert.Equal(3, await database.GetStockAsync(productId));
        await using var db = database.CreateDbContext();
        var stored = await db.Orders.Include(o => o.CancellationRequests).SingleAsync(o => o.Id == order.Id);
        Assert.Equal(OrderStatus.Paid, stored.Status);
        var request = Assert.Single(stored.CancellationRequests);
        Assert.Equal(outcome.Protocol, request.Protocol);
        Assert.Equal("Comprei o tamanho errado", request.Reason);
        Assert.Equal(OrderStatus.Paid, request.OrderStatusAtRequest);
        Assert.Equal(clock.Now.UtcDateTime, request.RequestedAt);
        var entry = await db.AuditEntries.SingleAsync(a => a.Action == AuditActions.OrderCancellationRequested && a.EntityId == order.Id.ToString());
        Assert.DoesNotContain("tamanho", entry.Details);
    }

    [FactWithDatabase]
    public async Task Second_request_while_one_is_open_is_refused()
    {
        var service = Service();
        var (customerId, _, order) = await CreateOrderAsync(service, paid: true);
        await service.RequestCancellationAsync(order.Code, customerId, null);

        await Assert.ThrowsAsync<OrderRuleException>(() => service.RequestCancellationAsync(order.Code, customerId, null));
    }

    [FactWithDatabase]
    public async Task Another_customer_cannot_request_cancellation()
    {
        var service = Service();
        var (_, _, order) = await CreateOrderAsync(service, paid: true);
        var stranger = await database.CreateCustomerAsync();

        var ex = await Assert.ThrowsAsync<OrderRuleException>(() => service.RequestCancellationAsync(order.Code, stranger.Id, null));
        Assert.Equal("Order not found.", ex.Message);
    }

    [FactWithDatabase]
    public async Task Cancelling_on_the_panel_answers_the_open_request()
    {
        var service = Service();
        var (customerId, _, order) = await CreateOrderAsync(service, paid: true);
        await service.RequestCancellationAsync(order.Code, customerId, null);

        await service.CancelAsync(order.Id, "Pedido da cliente");

        var stored = await service.GetAsync(order.Id);
        Assert.Null(stored!.PendingCancellationRequest);
        var request = Assert.Single(stored.CancellationRequests);
        Assert.NotNull(request.ResolvedAt);
        Assert.Equal("Pedido da cliente", request.ResolutionNote);
    }

    [FactWithDatabase]
    public async Task Team_can_answer_the_request_without_cancelling()
    {
        var service = Service();
        var (customerId, _, order) = await CreateOrderAsync(service, paid: true);
        await service.RequestCancellationAsync(order.Code, customerId, null);

        await service.ResolveCancellationRequestAsync(order.Id, "Cliente desistiu de cancelar");

        var stored = await service.GetAsync(order.Id);
        Assert.Equal(OrderStatus.Paid, stored!.Status);
        Assert.Equal("Cliente desistiu de cancelar", Assert.Single(stored.CancellationRequests).ResolutionNote);
        await Assert.ThrowsAsync<OrderRuleException>(() => service.ResolveCancellationRequestAsync(order.Id, null));
    }

    [FactWithDatabase]
    public async Task Dashboard_lists_open_requests_until_they_are_answered()
    {
        var service = Service();
        var dashboard = new DashboardService(database, clock);
        var (customerId, _, order) = await CreateOrderAsync(service, paid: true);
        var outcome = await service.RequestCancellationAsync(order.Code, customerId, null);

        var pending = (await dashboard.GetSummaryAsync()).PendingCancellations;
        var line = Assert.Single(pending, p => p.OrderId == order.Id);
        Assert.Equal(outcome.Protocol, line.Protocol);
        Assert.Equal(order.Code, line.OrderCode);

        await service.ResolveCancellationRequestAsync(order.Id, null);

        Assert.DoesNotContain((await dashboard.GetSummaryAsync()).PendingCancellations, p => p.OrderId == order.Id);
    }
}
