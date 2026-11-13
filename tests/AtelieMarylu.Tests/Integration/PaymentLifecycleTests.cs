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

// Provedor de mentira que abre "checkouts" e anota quais foram fechados (inativados)
public class RecordingPaymentGateway : IPaymentGateway
{
    private int next;
    public List<string> Cancelled { get; } = [];
    public bool FailOnCancel { get; set; }

    // Conciliação: o que o "provedor" responde quando a loja pergunta (por ExternalId)
    public Dictionary<string, PaymentNotification> StatusInProvider { get; } = [];
    public bool FailOnQuery { get; set; }
    public int Queries { get; private set; }

    public string Name => "recording";
    public bool VerifiesNotifications { get; set; } = true;

    // Webhook: o que o "provedor" responde para um aviso (por padrão, nada a fazer) e quantos avisos ele leu
    public Func<PaymentWebhookRequest, PaymentNotification?> OnNotification { get; set; } = _ => null;
    public int NotificationsRead { get; private set; }

    public string? ClaimedExternalId(PaymentWebhookRequest request) =>
        request.Query.TryGetValue("ref", out var reference) ? reference : null;

    public Task<PaymentStart> StartAsync(ChargeRequest charge, CancellationToken ct = default)
    {
        var n = Interlocked.Increment(ref next);
        return Task.FromResult(new PaymentStart($"ref-{charge.PaymentId}", $"https://pagamento.pagseguro.uol.com.br/pagamento?code={n}", null, $"CHEC_{n}"));
    }

    public Task CancelChargeAsync(string? providerReference, CancellationToken ct = default)
    {
        if (FailOnCancel) throw new PaymentGatewayException("provedor fora do ar");
        if (providerReference is not null) Cancelled.Add(providerReference);
        return Task.CompletedTask;
    }

    public Task<PaymentNotification?> QueryStatusAsync(string externalId, string? providerReference, CancellationToken ct = default)
    {
        Queries++;
        if (FailOnQuery) throw new PaymentGatewayException("provedor fora do ar");
        return Task.FromResult(StatusInProvider.GetValueOrDefault(externalId));
    }

    public Task<PaymentNotification?> ReadNotificationAsync(PaymentWebhookRequest request, CancellationToken ct = default)
    {
        NotificationsRead++;
        return Task.FromResult(OnNotification(request));
    }
}

// Nos testes, a cobrança é fechada na hora, e não numa fila em segundo plano. Falha do provedor é engolida
// como na fila de verdade (que só registra o alerta): a alteração do pedido nunca volta atrás.
public class InlineChargeCloser(IPaymentGateway gateway) : IChargeCloser
{
    // As falhas ficam registradas, como o ALERTA no log da fila de verdade, para os testes poderem conferir
    public List<string> Failures { get; } = [];

    public void Enqueue(ChargeToClose charge)
    {
        try
        {
            gateway.CancelChargeAsync(charge.ProviderReference).GetAwaiter().GetResult();
        }
        catch (PaymentGatewayException ex)
        {
            Failures.Add($"{charge.ProviderReference}: {ex.Message}");
        }
    }
}

// Ciclo de vida do pagamento junto com o provedor: cobrança cancelada aqui é fechada lá,
// e estorno ou contestação (chargeback) muda o pedido.
public class PaymentLifecycleTests(TestDatabase database) : IClassFixture<TestDatabase>
{
    private readonly FakeClock clock = new();
    private readonly RecordingPaymentGateway gateway = new();
    private readonly ShopOptions store = new() { PixDiscountPercent = 5, PaymentDeadlineMinutes = 120, MaxPendingOrdersPerCustomer = 20 };

    private OrderService Service()
    {
        var audit = new AuditService(new CurrentUserService(new ServiceCollection().BuildServiceProvider(), new HttpContextAccessor()), database);
        var localizer = new ServiceCollection().AddLogging().AddLocalization(o => o.ResourcesPath = "Resources").BuildServiceProvider()
            .GetRequiredService<IStringLocalizer<SharedResource>>();
        return new OrderService(database, new SimulatedShippingCalculator(), gateway, new InlineChargeCloser(gateway), audit, new OrderEvents(),
            Options.Create(store), clock, localizer, NullLogger<OrderService>.Instance);
    }

    private static ShippingAddress Address() => new()
    {
        PostalCode = "20000-000", Street = "Rua A", Number = "1", District = "Centro", City = "Rio de Janeiro", State = "RJ",
    };

    private async Task<(OrderService Service, Order Order, int ProductId)> NewOrderAsync()
    {
        var customer = await database.CreateCustomerAsync();
        var product = await database.CreateProductAsync(stock: 5);
        var service = Service();
        var order = await service.CreateAsync(customer.Id, [new CartItem(product.Id, 1)], Address(), "pac");
        return (service, order, product.Id);
    }

    private static PaymentNotification Approved(Payment payment) =>
        new($"{payment.ExternalId}:PAID", payment.ExternalId!, PaymentStatus.Approved, payment.Amount);

    private static PaymentNotification Refunded(Payment payment) =>
        new($"{payment.ExternalId}:REFUNDED", payment.ExternalId!, PaymentStatus.Refunded, payment.Amount);

    [FactWithDatabase]
    public async Task New_payment_closes_the_previous_checkout()
    {
        // A cliente abriu o PIX, desistiu e escolheu cartão: o checkout do PIX não pode continuar pagável
        var (service, order, _) = await NewOrderAsync();
        var pix = await service.StartPaymentAsync(order.Id, PaymentMethod.Pix);

        await service.StartPaymentAsync(order.Id, PaymentMethod.CreditCard);

        Assert.Equal([pix.ProviderReference!], gateway.Cancelled);
    }

    [FactWithDatabase]
    public async Task New_payment_is_not_opened_when_the_previous_checkout_cannot_be_closed()
    {
        var (service, order, _) = await NewOrderAsync();
        var pix = await service.StartPaymentAsync(order.Id, PaymentMethod.Pix);
        gateway.FailOnCancel = true;

        await Assert.ThrowsAsync<PaymentGatewayException>(() => service.StartPaymentAsync(order.Id, PaymentMethod.CreditCard));

        await using var db = database.CreateDbContext();
        var payments = await db.Payments.Where(p => p.OrderId == order.Id).ToListAsync();
        var only = Assert.Single(payments);
        Assert.Equal(PaymentStatus.Pending, only.Status);
        Assert.Equal(pix.Id, only.Id);
    }

    [FactWithDatabase]
    public async Task Cancelling_the_order_closes_the_open_checkout()
    {
        var (service, order, _) = await NewOrderAsync();
        var pix = await service.StartPaymentAsync(order.Id, PaymentMethod.Pix);

        await service.CancelAsync(order.Id);

        Assert.Contains(pix.ProviderReference!, gateway.Cancelled);
    }

    [FactWithDatabase]
    public async Task Manual_payment_closes_the_open_checkout()
    {
        // Pagou na maquininha (Minizinha) e a loja registrou à mão: o link do PagBank deixa de valer
        var (service, order, _) = await NewOrderAsync();
        var pix = await service.StartPaymentAsync(order.Id, PaymentMethod.Pix);

        await service.RecordManualPaymentAsync(order.Id, PaymentMethod.CreditCard);

        Assert.Contains(pix.ProviderReference!, gateway.Cancelled);
    }

    [FactWithDatabase]
    public async Task Failure_to_close_after_cancelling_does_not_undo_the_cancellation()
    {
        var (service, order, _) = await NewOrderAsync();
        await service.StartPaymentAsync(order.Id, PaymentMethod.Pix);
        gateway.FailOnCancel = true;

        await service.CancelAsync(order.Id);

        Assert.Equal(OrderStatus.Cancelled, (await service.GetAsync(order.Id))!.Status);
    }

    [FactWithDatabase]
    public async Task Expired_orders_do_not_call_the_provider()
    {
        // O checkout vence junto com o prazo do pedido: não há o que fechar
        var (service, order, _) = await NewOrderAsync();
        await service.StartPaymentAsync(order.Id, PaymentMethod.Pix);
        clock.Now = clock.Now.AddMinutes(store.PaymentDeadlineMinutes + 1);

        await service.ExpireOverdueAsync();

        Assert.Empty(gateway.Cancelled);
    }

    [FactWithDatabase]
    public async Task Paid_checkout_is_closed_so_it_cannot_be_paid_again()
    {
        // No sandbox, o checkout do PagBank continuou ativo depois de pago: a cliente pagaria duas vezes no mesmo link
        var (service, order, _) = await NewOrderAsync();
        var pix = await service.StartPaymentAsync(order.Id, PaymentMethod.Pix);

        await service.ProcessNotificationAsync(Approved(pix));

        Assert.Equal([pix.ProviderReference!], gateway.Cancelled);
    }

    [FactWithDatabase]
    public async Task Order_paid_without_notice_is_confirmed_instead_of_expiring()
    {
        // O aviso do provedor se perdeu (ex.: a loja estava fora do ar num deploy), mas o pagamento entrou
        var (service, order, productId) = await NewOrderAsync();
        var pix = await service.StartPaymentAsync(order.Id, PaymentMethod.Pix);
        gateway.StatusInProvider[pix.ExternalId!] = Approved(pix);
        clock.Now = clock.Now.AddMinutes(store.PaymentDeadlineMinutes + 1);

        var expired = await service.ExpireOverdueAsync();

        Assert.Equal(0, expired);
        Assert.Equal(OrderStatus.Paid, (await service.GetAsync(order.Id))!.Status);
        Assert.Equal(4, await database.GetStockAsync(productId));
    }

    [FactWithDatabase]
    public async Task Unpaid_order_expires_after_asking_the_provider()
    {
        var (service, order, productId) = await NewOrderAsync();
        await service.StartPaymentAsync(order.Id, PaymentMethod.Pix);
        clock.Now = clock.Now.AddMinutes(store.PaymentDeadlineMinutes + 1);

        await service.ExpireOverdueAsync();

        Assert.Equal(1, gateway.Queries);
        Assert.Equal(OrderStatus.Cancelled, (await service.GetAsync(order.Id))!.Status);
        Assert.Equal(5, await database.GetStockAsync(productId));
    }

    [FactWithDatabase]
    public async Task Provider_down_postpones_expiry_but_not_forever()
    {
        var (service, order, _) = await NewOrderAsync();
        await service.StartPaymentAsync(order.Id, PaymentMethod.Pix);
        gateway.FailOnQuery = true;

        // Dentro da folga: não expira, porque pode ter sido pago
        clock.Now = clock.Now.AddMinutes(store.PaymentDeadlineMinutes + 1);
        await service.ExpireOverdueAsync();
        Assert.Equal(OrderStatus.AwaitingPayment, (await service.GetAsync(order.Id))!.Status);

        // Passada a folga: expira assim mesmo, para não segurar o estoque para sempre
        clock.Now = clock.Now.Add(OrderService.ReconciliationGrace);
        await service.ExpireOverdueAsync();
        Assert.Equal(OrderStatus.Cancelled, (await service.GetAsync(order.Id))!.Status);
    }

    [FactWithDatabase]
    public async Task Shipping_is_blocked_when_the_provider_shows_a_refund()
    {
        // O aviso de estorno se perdeu: a conferência antes do envio pega e cancela o pedido
        var (service, order, productId) = await NewOrderAsync();
        var pix = await service.StartPaymentAsync(order.Id, PaymentMethod.Pix);
        await service.ProcessNotificationAsync(Approved(pix));
        gateway.StatusInProvider[pix.ExternalId!] = Refunded(pix);

        await Assert.ThrowsAsync<OrderRuleException>(() => service.MarkShippedAsync(order.Id, "BR123"));

        Assert.Equal(OrderStatus.Cancelled, (await service.GetAsync(order.Id))!.Status);
        Assert.Equal(5, await database.GetStockAsync(productId));
    }

    [FactWithDatabase]
    public async Task Shipping_waits_when_the_provider_does_not_answer()
    {
        var (service, order, _) = await NewOrderAsync();
        var pix = await service.StartPaymentAsync(order.Id, PaymentMethod.Pix);
        await service.ProcessNotificationAsync(Approved(pix));
        gateway.FailOnQuery = true;

        await Assert.ThrowsAsync<OrderRuleException>(() => service.MarkShippedAsync(order.Id, "BR123"));

        Assert.Equal(OrderStatus.Paid, (await service.GetAsync(order.Id))!.Status);
    }

    [FactWithDatabase]
    public async Task Shipping_goes_ahead_when_the_payment_is_still_valid()
    {
        var (service, order, _) = await NewOrderAsync();
        var pix = await service.StartPaymentAsync(order.Id, PaymentMethod.Pix);
        await service.ProcessNotificationAsync(Approved(pix));
        gateway.StatusInProvider[pix.ExternalId!] = Approved(pix);

        await service.MarkShippedAsync(order.Id, "BR123");

        Assert.Equal(OrderStatus.Shipped, (await service.GetAsync(order.Id))!.Status);
    }

    [FactWithDatabase]
    public async Task Manual_payment_ships_without_asking_the_provider()
    {
        // Pago na Minizinha e registrado à mão: não há o que consultar
        var (service, order, _) = await NewOrderAsync();
        await service.RecordManualPaymentAsync(order.Id, PaymentMethod.CreditCard);
        gateway.FailOnQuery = true;

        await service.MarkShippedAsync(order.Id, null);

        Assert.Equal(OrderStatus.Shipped, (await service.GetAsync(order.Id))!.Status);
        Assert.Equal(0, gateway.Queries);
    }

    [FactWithDatabase]
    public async Task Refund_of_paid_order_cancels_it_returns_stock_and_alerts()
    {
        var (service, order, productId) = await NewOrderAsync();
        var pix = await service.StartPaymentAsync(order.Id, PaymentMethod.Pix);
        await service.ProcessNotificationAsync(Approved(pix));
        Assert.Equal(4, await database.GetStockAsync(productId));

        await service.ProcessNotificationAsync(Refunded(pix));

        var stored = (await service.GetAsync(order.Id))!;
        Assert.Equal(OrderStatus.Cancelled, stored.Status);
        Assert.Equal(PaymentStatus.Refunded, stored.Payments.Single().Status);
        Assert.Equal(5, await database.GetStockAsync(productId));
        await using var db = database.CreateDbContext();
        var alert = await db.AuditEntries.SingleAsync(a => a.Action == AuditActions.PaymentReversed && a.EntityId == order.Id.ToString());
        Assert.Contains("Não envie", alert.Details);
    }

    [FactWithDatabase]
    public async Task Refund_of_shipped_order_keeps_it_and_alerts()
    {
        var (service, order, productId) = await NewOrderAsync();
        var pix = await service.StartPaymentAsync(order.Id, PaymentMethod.Pix);
        await service.ProcessNotificationAsync(Approved(pix));
        await service.MarkShippedAsync(order.Id, "BR123");

        await service.ProcessNotificationAsync(Refunded(pix));

        Assert.Equal(OrderStatus.Shipped, (await service.GetAsync(order.Id))!.Status);
        Assert.Equal(4, await database.GetStockAsync(productId));
        await using var db = database.CreateDbContext();
        var alert = await db.AuditEntries.SingleAsync(a => a.Action == AuditActions.PaymentReversed && a.EntityId == order.Id.ToString());
        Assert.Contains("já foi enviado", alert.Details);
    }

    [FactWithDatabase]
    public async Task Cancellation_of_an_approved_payment_raises_an_alert_without_changing_the_order()
    {
        // Formato inesperado de estorno: "cancelado" para um pagamento já aprovado não pode passar em silêncio
        var (service, order, _) = await NewOrderAsync();
        var pix = await service.StartPaymentAsync(order.Id, PaymentMethod.Pix);
        await service.ProcessNotificationAsync(Approved(pix));

        await service.ProcessNotificationAsync(new PaymentNotification($"{pix.ExternalId}:CANCELED", pix.ExternalId!, PaymentStatus.Cancelled));

        Assert.Equal(OrderStatus.Paid, (await service.GetAsync(order.Id))!.Status);
        await using var db = database.CreateDbContext();
        var alert = await db.AuditEntries.SingleAsync(a => a.Action == AuditActions.PaymentReversed && a.EntityId == order.Id.ToString());
        Assert.Contains("Confira no painel do provedor", alert.Details);
    }

    [FactWithDatabase]
    public async Task Repeated_refund_notice_is_processed_once()
    {
        var (service, order, productId) = await NewOrderAsync();
        var pix = await service.StartPaymentAsync(order.Id, PaymentMethod.Pix);
        await service.ProcessNotificationAsync(Approved(pix));

        await service.ProcessNotificationAsync(Refunded(pix));
        await service.ProcessNotificationAsync(Refunded(pix));

        // O estoque volta uma vez só
        Assert.Equal(5, await database.GetStockAsync(productId));
    }
}
