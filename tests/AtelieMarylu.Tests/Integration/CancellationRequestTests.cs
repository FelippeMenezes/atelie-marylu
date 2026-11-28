using AtelieMarylu.Domain.Auditing;
using AtelieMarylu.Domain.Carts;
using AtelieMarylu.Domain.Orders;
using AtelieMarylu.Domain.Payments;
using AtelieMarylu.Domain.Shipping;
using AtelieMarylu.Infrastructure.Data;
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
    private readonly ShopOptions store = new() { PaymentDeadlineMinutes = 120, MaxPendingOrdersPerCustomer = 3, ContactEmail = "contato@loja.teste" };

    private OrderService Service(IDbContextFactory<ApplicationDbContext>? factory = null)
    {
        var audit = new AuditService(new CurrentUserService(new ServiceCollection().BuildServiceProvider(), new HttpContextAccessor()), database);
        var gateway = new SimulatedPaymentGateway();
        var localizer = new ServiceCollection().AddLogging().AddLocalization(o => o.ResourcesPath = "Resources").BuildServiceProvider()
            .GetRequiredService<IStringLocalizer<SharedResource>>();
        return new OrderService(factory ?? database, new SimulatedShippingCalculator(), gateway, new InlineChargeCloser(gateway), audit, new OrderEvents(),
            new OrderEmails(Options.Create(store), new EmailOutboxSignal(), clock, NullLogger<OrderEmails>.Instance),
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
    public async Task Unpaid_order_is_cancelled_now_with_fixed_reason_and_stock_returns()
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
        // Texto fixo: o motivo livre ficaria no pedido (guardado por obrigação fiscal) e na auditoria depois da exclusão da conta
        Assert.Equal(OrderService.CustomerCancellationReason, stored.CancellationReason);
        Assert.Empty(stored.CancellationRequests);
        Assert.False(await db.AuditEntries.AnyAsync(a => a.EntityId == order.Id.ToString() && a.Details!.Contains("ideia")));
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

    [FactWithDatabase]
    public async Task Database_refuses_a_second_open_request_for_the_same_order()
    {
        // A regra "uma solicitação aberta por vez" também vale fora do código (ex.: duas gravações ao mesmo tempo)
        var service = Service();
        var (customerId, _, order) = await CreateOrderAsync(service, paid: true);
        await service.RequestCancellationAsync(order.Code, customerId, null);

        await using var db = database.CreateDbContext();
        var ex = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            """INSERT INTO "CancellationRequests" ("OrderId", "Protocol", "RequestedAt", "OrderStatusAtRequest") VALUES ({0}, 'CAN-TESTE2', now(), 'Paid')""",
            order.Id));
        Assert.Equal(Npgsql.PostgresErrorCodes.UniqueViolation, ex.SqlState);
        Assert.Equal("IX_CancellationRequests_OrderId_Open", ex.ConstraintName);
    }

    [FactWithDatabase]
    public async Task Request_writes_both_emails_to_the_outbox_without_the_reason()
    {
        var service = Service();
        var (customerId, _, order) = await CreateOrderAsync(service, paid: true);

        var outcome = await service.RequestCancellationAsync(order.Code, customerId, "  cor errada ");

        await using var db = database.CreateDbContext();
        var customer = await db.Users.SingleAsync(u => u.Id == customerId);
        var emails = await db.OutboxEmails.Where(e => e.OrderId == order.Id && e.Kind.StartsWith("cancellation.")).ToListAsync();
        // Gravados na mesma transação da solicitação: a cliente com o nome, a loja só com identificadores
        var toCustomer = Assert.Single(emails, e => e.Kind == OutboxEmailKinds.CancellationReceived);
        Assert.Equal(customer.Email, toCustomer.Recipient);
        Assert.Contains(outcome.Protocol!, toCustomer.Payload);
        var toStore = Assert.Single(emails, e => e.Kind == OutboxEmailKinds.CancellationStaffNotice);
        Assert.Equal(store.ContactEmail, toStore.Recipient);
        Assert.DoesNotContain(customer.FullName, toStore.Payload);
        // O motivo fica só no banco: nenhum e-mail o carrega
        Assert.All(emails, e => Assert.DoesNotContain("cor errada", e.Payload));
        Assert.Equal("cor errada", (await db.CancellationRequests.SingleAsync(r => r.OrderId == order.Id)).Reason);
        Assert.Equal(OrderStatus.Paid, outcome.OrderStatusAtRequest);
    }

    [FactWithDatabase]
    public async Task Unpaid_order_cancelled_now_writes_no_cancellation_email()
    {
        var service = Service();
        var (customerId, _, order) = await CreateOrderAsync(service, paid: false);

        await service.RequestCancellationAsync(order.Code, customerId, null);

        await using var db = database.CreateDbContext();
        Assert.False(await db.OutboxEmails.AnyAsync(e => e.OrderId == order.Id && e.Kind.StartsWith("cancellation.")));
    }

    [FactWithDatabase]
    public async Task Request_based_on_a_stale_order_fails_the_version_check()
    {
        // Reproduz a corrida que o RequestCancellationAsync evita: a cliente carregou o pedido, o admin cancelou
        // antes de ela gravar. Marcar o Status como alterado (sem mudar o valor) obriga o UPDATE com o xmin lido.
        var service = Service();
        var (_, _, order) = await CreateOrderAsync(service, paid: true);
        await using var stale = database.CreateDbContext();
        var loaded = await stale.Orders.Include(o => o.CancellationRequests).SingleAsync(o => o.Id == order.Id);

        await service.CancelAsync(order.Id, "Cancelado pelo admin");

        loaded.RequestCancellation(null, clock.Now.UtcDateTime);
        stale.Entry(loaded).Property(o => o.Status).IsModified = true;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
    }

    [FactWithDatabase]
    public async Task Payment_confirmed_while_cancelling_turns_into_a_request_instead_of_an_error()
    {
        // A cliente clica "Cancelar" num pedido não pago, e o pagamento é confirmado no meio da gravação dela.
        // A primeira tentativa bate no xmin; a segunda relê o pedido (já pago) e registra uma solicitação.
        var setup = Service();
        var (customerId, productId, order) = await CreateOrderAsync(setup, paid: false);
        var interfering = new InterferingFactory(database, () => setup.RecordManualPaymentAsync(order.Id, PaymentMethod.Pix));

        var outcome = await Service(interfering).RequestCancellationAsync(order.Code, customerId, null);

        Assert.Equal(1, interfering.Interferences);
        Assert.False(outcome.CancelledNow);
        Assert.StartsWith("CAN-", outcome.Protocol);
        Assert.Equal(OrderStatus.Paid, outcome.OrderStatusAtRequest);
        var stored = await setup.GetAsync(order.Id);
        Assert.Equal(OrderStatus.Paid, stored!.Status);
        Assert.NotNull(stored.PendingCancellationRequest);
        // O estoque não voltou: o cancelamento da primeira tentativa não foi gravado
        Assert.Equal(3, await database.GetStockAsync(productId));
    }

    [FactWithDatabase]
    public async Task Repeated_conflicts_end_in_a_translated_message()
    {
        var setup = Service();
        var (customerId, _, order) = await CreateOrderAsync(setup, paid: true);
        // Muda o pedido antes de toda gravação da cliente: as três tentativas batem no xmin
        var interfering = new InterferingFactory(database, () => setup.ChangeDeliveryAsync(order.Id, Address(), $"nota {Guid.NewGuid()}"),
            everyTime: true);

        var ex = await Assert.ThrowsAsync<OrderRuleException>(() => Service(interfering).RequestCancellationAsync(order.Code, customerId, null));

        Assert.Equal("This order was changed at the same time. Reload the page and try again.", ex.Message);
        Assert.Equal(3, interfering.Interferences);
        Assert.Null((await setup.GetAsync(order.Id))!.PendingCancellationRequest);
    }

    // Fábrica que, logo antes de a cliente gravar, altera o pedido por outro caminho (outra conexão, já confirmada):
    // reproduz de forma determinística a corrida que em produção depende de milissegundos
    private sealed class InterferingFactory(TestDatabase database, Func<Task> interfere, bool everyTime = false)
        : IDbContextFactory<ApplicationDbContext>
    {
        public int Interferences { get; private set; }

        public ApplicationDbContext CreateDbContext()
        {
            var db = database.CreateDbContext();
            db.SavingChanges += (_, _) =>
            {
                if (!everyTime && Interferences > 0) return;
                Interferences++;
                interfere().GetAwaiter().GetResult();
            };
            return db;
        }
    }
}
