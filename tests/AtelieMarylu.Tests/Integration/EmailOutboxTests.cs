using System.Globalization;
using AtelieMarylu.Domain.Carts;
using AtelieMarylu.Domain.Email;
using AtelieMarylu.Domain.Orders;
using AtelieMarylu.Domain.Payments;
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

// Outbox transacional dos e-mails (AML-0041): o OrderService grava a intenção junto com a mudança do pedido, e o
// EmailOutboxDispatcher entrega, tenta de novo ou desiste. Os testes de uma classe rodam em sequência, no mesmo banco.
public class EmailOutboxTests(TestDatabase database) : IClassFixture<TestDatabase>
{
    private readonly FakeClock clock = new();
    private readonly RecordingPaymentGateway gateway = new();
    private readonly ShopOptions store = new()
    {
        PixDiscountPercent = 5, PaymentDeadlineMinutes = 120, MaxPendingOrdersPerCustomer = 20, PublicBaseUrl = "https://loja.teste",
    };
    private readonly FakeEmailClient client = new();
    private readonly EmailOutboxSignal signal = new();

    private OrderService Service()
    {
        var audit = new AuditService(new CurrentUserService(new ServiceCollection().BuildServiceProvider(), new HttpContextAccessor()), database);
        return new OrderService(database, new SimulatedShippingCalculator(), gateway, new InlineChargeCloser(gateway), audit, new OrderEvents(),
            new OrderEmails(Options.Create(store), signal, clock, NullLogger<OrderEmails>.Instance),
            Options.Create(store), clock, Services().GetRequiredService<IStringLocalizer<SharedResource>>(), NullLogger<OrderService>.Instance);
    }

    private ServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization(o => o.ResourcesPath = "Resources");
        services.AddSingleton(Options.Create(store));
        services.AddSingleton<IDbContextFactory<ApplicationDbContext>>(database);
        services.AddSingleton<IEmailClient>(client);
        services.AddScoped<OrderEmailRenderer>();
        return services.BuildServiceProvider();
    }

    private EmailOutboxDispatcher Dispatcher() =>
        new(Services().GetRequiredService<IServiceScopeFactory>(), new EmailOutboxSignal(), clock, NullLogger<EmailOutboxDispatcher>.Instance);

    private static ShippingAddress Address() => new()
    {
        PostalCode = "20000-000", Street = "Rua A", Number = "1", District = "Centro", City = "Rio de Janeiro", State = "RJ",
    };

    private async Task<(OrderService Service, Order Order, ApplicationUser Customer)> NewOrderAsync(string culture = "pt-BR", string? createdBy = null)
    {
        var customer = await database.CreateCustomerAsync();
        var product = await database.CreateProductAsync(stock: 5);
        var service = Service();
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            var order = await service.CreateAsync(customer.Id, [new CartItem(product.Id, 1)], Address(), "pac", createdBy);
            return (service, order, customer);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    private async Task<List<OutboxEmail>> OutboxAsync(int orderId)
    {
        await using var db = database.CreateDbContext();
        return await db.OutboxEmails.AsNoTracking().Where(e => e.OrderId == orderId).OrderBy(e => e.CreatedAt).ToListAsync();
    }

    private async Task<OutboxEmail> InsertAsync(string kind = OutboxEmailKinds.CancellationReceived, DateTime? nextAttemptAt = null)
    {
        await using var db = database.CreateDbContext();
        var now = clock.GetUtcNow().UtcDateTime;
        var email = new OutboxEmail
        {
            Kind = kind, Recipient = "cliente@teste.local", Language = "pt-BR", OrderId = 1, CreatedAt = now,
            NextAttemptAt = nextAttemptAt ?? now,
            Payload = """{"orderId":1,"orderCode":"AML-X1","customerName":"Maria","protocol":"CAN-1","at":"2026-11-25T10:00:00Z","orderStatusAtRequest":"Paid"}""",
        };
        db.OutboxEmails.Add(email);
        await db.SaveChangesAsync();
        return email;
    }

    private async Task<OutboxEmail> ReloadAsync(Guid id)
    {
        await using var db = database.CreateDbContext();
        return await db.OutboxEmails.AsNoTracking().SingleAsync(e => e.Id == id);
    }

    private async Task ClearOutboxAsync()
    {
        await using var db = database.CreateDbContext();
        await db.OutboxEmails.ExecuteDeleteAsync();
    }

    [FactWithDatabase]
    public async Task New_order_writes_order_received_in_the_customer_language()
    {
        var (_, order, customer) = await NewOrderAsync(culture: "es");

        var email = Assert.Single(await OutboxAsync(order.Id));
        Assert.Equal(OutboxEmailKinds.OrderReceived, email.Kind);
        Assert.Equal(customer.Email, email.Recipient);
        Assert.Equal("es", email.Language);
        Assert.Contains(order.Code, email.Payload);
        Assert.Null(email.SentAt);
    }

    [FactWithDatabase]
    public async Task Order_placed_by_the_staff_uses_the_store_language()
    {
        // O idioma do painel é o da funcionária, não o da cliente
        var (_, order, _) = await NewOrderAsync(culture: "en", createdBy: "admin@teste.local");

        Assert.Equal("pt-BR", Assert.Single(await OutboxAsync(order.Id)).Language);
    }

    [FactWithDatabase]
    public async Task Approved_payment_writes_payment_confirmed_once()
    {
        var (service, order, _) = await NewOrderAsync();
        var payment = await service.StartPaymentAsync(order.Id, PaymentMethod.Pix);

        await service.ProcessNotificationAsync(new($"{payment.ExternalId}:PAID", payment.ExternalId!, PaymentStatus.Approved, payment.Amount));
        // Outro aviso de aprovado (outro EventId) para o mesmo pedido: já estava pago, nada de segundo e-mail
        await service.ProcessNotificationAsync(new($"{payment.ExternalId}:PAID-2", payment.ExternalId!, PaymentStatus.Approved, payment.Amount));

        var paid = Assert.Single(await OutboxAsync(order.Id), e => e.Kind == OutboxEmailKinds.PaymentConfirmed);
        Assert.Contains(payment.Amount.ToString(CultureInfo.InvariantCulture), paid.Payload);
    }

    [FactWithDatabase]
    public async Task Manual_payment_also_writes_payment_confirmed()
    {
        var (service, order, _) = await NewOrderAsync();

        await service.RecordManualPaymentAsync(order.Id, PaymentMethod.CreditCard);

        Assert.Single(await OutboxAsync(order.Id), e => e.Kind == OutboxEmailKinds.PaymentConfirmed);
    }

    [FactWithDatabase]
    public async Task Order_received_stops_at_the_limit_for_the_same_recipient()
    {
        var customer = await database.CreateCustomerAsync();
        var product = await database.CreateProductAsync(stock: 100);
        var service = Service();

        for (var i = 0; i <= OrderEmails.MaxOrderReceivedPerDay; i++)
        {
            var order = await service.CreateAsync(customer.Id, [new CartItem(product.Id, 1)], Address(), "pac");
            await service.CancelAsync(order.Id);
        }

        await using var db = database.CreateDbContext();
        Assert.Equal(OrderEmails.MaxOrderReceivedPerDay,
            await db.OutboxEmails.CountAsync(e => e.Recipient == customer.Email && e.Kind == OutboxEmailKinds.OrderReceived));
    }

    [FactWithDatabase]
    public async Task Order_received_limit_slides_with_the_last_24_hours()
    {
        var customer = await database.CreateCustomerAsync();
        var product = await database.CreateProductAsync(stock: 100);
        var service = Service();
        async Task CreateAndCancelAsync() =>
            await service.CancelAsync((await service.CreateAsync(customer.Id, [new CartItem(product.Id, 1)], Address(), "pac")).Id);

        for (var i = 0; i < OrderEmails.MaxOrderReceivedPerDay; i++)
            await CreateAndCancelAsync();

        // Um minuto antes de completar 24 h, ainda barra; logo depois, libera de novo
        clock.Now += OrderEmails.OrderReceivedWindow - TimeSpan.FromMinutes(1);
        await CreateAndCancelAsync();
        clock.Now += TimeSpan.FromMinutes(2);
        await CreateAndCancelAsync();

        await using var db = database.CreateDbContext();
        Assert.Equal(OrderEmails.MaxOrderReceivedPerDay + 1,
            await db.OutboxEmails.CountAsync(e => e.Recipient == customer.Email && e.Kind == OutboxEmailKinds.OrderReceived));
    }

    [FactWithDatabase]
    public async Task Order_received_limit_is_per_recipient()
    {
        // O limite de uma pessoa não atinge outra
        var product = await database.CreateProductAsync(stock: 100);
        var service = Service();
        var first = await database.CreateCustomerAsync();
        for (var i = 0; i < OrderEmails.MaxOrderReceivedPerDay; i++)
            await service.CancelAsync((await service.CreateAsync(first.Id, [new CartItem(product.Id, 1)], Address(), "pac")).Id);

        var other = await database.CreateCustomerAsync();
        var order = await service.CreateAsync(other.Id, [new CartItem(product.Id, 1)], Address(), "pac");

        Assert.Single(await OutboxAsync(order.Id), e => e.Kind == OutboxEmailKinds.OrderReceived);
    }

    [FactWithDatabase]
    public async Task Worker_is_woken_only_when_a_change_writes_an_email()
    {
        var (service, order, _) = await NewOrderAsync();
        Assert.True(await signal.WaitAsync(TimeSpan.Zero, CancellationToken.None)); // "pedido recebido"

        // Cancelar um pedido não pago não manda e-mail: nada de acordar o worker à toa
        var (other, unpaid, _) = await NewOrderAsync();
        await signal.WaitAsync(TimeSpan.Zero, CancellationToken.None);
        await other.CancelAsync(unpaid.Id);
        Assert.False(await signal.WaitAsync(TimeSpan.Zero, CancellationToken.None));

        // Pagamento registrado grava o "pagamento confirmado": acorda
        await service.RecordManualPaymentAsync(order.Id, PaymentMethod.Pix);
        Assert.True(await signal.WaitAsync(TimeSpan.Zero, CancellationToken.None));
    }

    [FactWithDatabase]
    public async Task Dispatcher_sends_and_marks_as_sent_with_the_idempotency_key()
    {
        await ClearOutboxAsync();
        var email = await InsertAsync();

        Assert.Equal(1, await Dispatcher().DispatchDueAsync(CancellationToken.None));

        var sent = Assert.Single(client.Sent);
        Assert.Equal($"outbox/{email.Id}", sent.IdempotencyKey);
        Assert.Contains("CAN-1", sent.Html);
        Assert.NotNull((await ReloadAsync(email.Id)).SentAt);
        // Na rodada seguinte, nada a fazer
        Assert.Equal(0, await Dispatcher().DispatchDueAsync(CancellationToken.None));
    }

    [FactWithDatabase]
    public async Task Row_not_due_yet_waits()
    {
        await ClearOutboxAsync();
        await InsertAsync(nextAttemptAt: clock.GetUtcNow().UtcDateTime.AddMinutes(5));

        Assert.Equal(0, await Dispatcher().DispatchDueAsync(CancellationToken.None));
        Assert.Empty(client.Sent);
    }

    [FactWithDatabase]
    public async Task Transient_failure_schedules_another_attempt()
    {
        await ClearOutboxAsync();
        var email = await InsertAsync();
        client.Failure = new EmailDeliveryException("Resend fora do ar (teste)", isTransient: true);

        await Dispatcher().DispatchDueAsync(CancellationToken.None);

        var stored = await ReloadAsync(email.Id);
        Assert.Equal(1, stored.Attempts);
        Assert.Null(stored.SentAt);
        Assert.Null(stored.FailedAt);
        Assert.True(stored.NextAttemptAt > clock.GetUtcNow().UtcDateTime);
        Assert.Equal("Resend fora do ar (teste)", stored.LastError);
    }

    [FactWithDatabase]
    public async Task Permanent_failure_gives_up_at_once()
    {
        await ClearOutboxAsync();
        var email = await InsertAsync();
        client.Failure = new EmailDeliveryException("Endereço inválido (teste)", isTransient: false);

        await Dispatcher().DispatchDueAsync(CancellationToken.None);

        var stored = await ReloadAsync(email.Id);
        Assert.NotNull(stored.FailedAt);
        Assert.Equal(1, stored.Attempts);
    }

    [FactWithDatabase]
    public async Task Row_that_cannot_be_rendered_gives_up_without_blocking_the_others()
    {
        await ClearOutboxAsync();
        var broken = await InsertAsync(kind: "unknown");
        var good = await InsertAsync();

        await Dispatcher().DispatchDueAsync(CancellationToken.None);

        Assert.NotNull((await ReloadAsync(broken.Id)).FailedAt);
        Assert.NotNull((await ReloadAsync(good.Id)).SentAt);
    }

    [FactWithDatabase]
    public async Task Order_received_is_skipped_when_the_order_is_no_longer_awaiting_payment()
    {
        // Ex.: o Resend ficou fora do ar e, quando voltou, o pedido já estava pago (ou expirado)
        var (service, order, _) = await NewOrderAsync();
        await service.RecordManualPaymentAsync(order.Id, PaymentMethod.Pix);
        await using (var db = database.CreateDbContext())
            await db.OutboxEmails.Where(e => e.OrderId != order.Id).ExecuteDeleteAsync();

        await Dispatcher().DispatchDueAsync(CancellationToken.None);

        var emails = await OutboxAsync(order.Id);
        var received = Assert.Single(emails, e => e.Kind == OutboxEmailKinds.OrderReceived);
        Assert.NotNull(received.SkippedAt);
        Assert.Null(received.FailedAt); // pular não é falha: não gera ALERTA
        Assert.Contains("Paid", received.LastError);
        // O "pagamento confirmado" sai normalmente
        Assert.NotNull(Assert.Single(emails, e => e.Kind == OutboxEmailKinds.PaymentConfirmed).SentAt);
        Assert.Single(client.Sent);
    }

    [FactWithDatabase]
    public async Task Order_received_goes_out_while_the_order_awaits_payment()
    {
        var (_, order, _) = await NewOrderAsync();
        await using (var db = database.CreateDbContext())
            await db.OutboxEmails.Where(e => e.OrderId != order.Id).ExecuteDeleteAsync();

        await Dispatcher().DispatchDueAsync(CancellationToken.None);

        Assert.NotNull(Assert.Single(await OutboxAsync(order.Id)).SentAt);
    }

    [FactWithDatabase]
    public async Task Sent_is_recorded_even_if_shutdown_arrives_right_after_the_send()
    {
        // O Resend aceitou e, no mesmo instante, a loja começou a desligar (deploy). Sem gravar o "enviado", a linha
        // sairia de novo na próxima subida, contando só com a chave de idempotência.
        await ClearOutboxAsync();
        var email = await InsertAsync();
        using var shutdown = new CancellationTokenSource();
        client.AfterSend = shutdown.Cancel;

        await Dispatcher().DispatchDueAsync(shutdown.Token);

        Assert.NotNull((await ReloadAsync(email.Id)).SentAt);
    }

    [FactWithDatabase]
    public async Task Cleanup_removes_personal_data_after_30_days_but_keeps_the_evidence()
    {
        // Dado pessoal expira, o fato fica: a loja continua podendo provar que mandou a confirmação
        await ClearOutboxAsync();
        var pending = await InsertAsync(nextAttemptAt: clock.GetUtcNow().UtcDateTime.AddDays(60));
        var sent = await InsertAsync();
        await Dispatcher().DispatchDueAsync(CancellationToken.None);
        var sentAt = (await ReloadAsync(sent.Id)).SentAt;

        clock.Now += EmailOutboxDispatcher.PersonalDataRetention + TimeSpan.FromMinutes(1);
        Assert.Equal(1, await Dispatcher().CleanupIfDueAsync(CancellationToken.None));

        var scrubbed = await ReloadAsync(sent.Id);
        Assert.Null(scrubbed.Recipient);
        Assert.Equal("{}", scrubbed.Payload);
        Assert.Null(scrubbed.LastError);
        Assert.Equal(sentAt, scrubbed.SentAt);
        Assert.Equal(OutboxEmailKinds.CancellationReceived, scrubbed.Kind);
        Assert.Equal(1, scrubbed.OrderId);
        // A pendente não é tocada
        Assert.Equal("cliente@teste.local", (await ReloadAsync(pending.Id)).Recipient);
    }

    [FactWithDatabase]
    public async Task Cleanup_removes_the_whole_row_after_the_evidence_period()
    {
        await ClearOutboxAsync();
        var sent = await InsertAsync();
        await Dispatcher().DispatchDueAsync(CancellationToken.None);

        clock.Now += EmailOutboxDispatcher.EvidenceRetention + TimeSpan.FromMinutes(1);
        await Dispatcher().CleanupIfDueAsync(CancellationToken.None);

        await using var db = database.CreateDbContext();
        Assert.False(await db.OutboxEmails.AnyAsync(e => e.Id == sent.Id));
    }

    [FactWithDatabase]
    public async Task Payment_confirmed_shows_only_the_payment_that_confirmed_the_order()
    {
        // 1º pagamento com valor errado: fica "aprovado", vira alerta e o pedido continua aguardando.
        // 2º pagamento certo confirma o pedido. O e-mail mostra só o 2º, e não a soma dos dois.
        var (service, order, _) = await NewOrderAsync();
        var wrong = await service.StartPaymentAsync(order.Id, PaymentMethod.Pix);
        await service.ProcessNotificationAsync(new($"{wrong.ExternalId}:PAID", wrong.ExternalId!, PaymentStatus.Approved, wrong.Amount - 1));
        Assert.Equal(OrderStatus.AwaitingPayment, (await service.GetAsync(order.Id))!.Status);

        var right = await service.StartPaymentAsync(order.Id, PaymentMethod.Pix);
        await service.ProcessNotificationAsync(new($"{right.ExternalId}:PAID", right.ExternalId!, PaymentStatus.Approved, right.Amount));

        var paid = Assert.Single(await OutboxAsync(order.Id), e => e.Kind == OutboxEmailKinds.PaymentConfirmed);
        var data = System.Text.Json.JsonSerializer.Deserialize<OrderEmailData>(paid.Payload!, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        })!;
        Assert.Equal(right.Amount, data.AmountPaid);
    }

    public sealed class FakeEmailClient : IEmailClient
    {
        public List<EmailMessage> Sent { get; } = [];
        public EmailDeliveryException? Failure { get; set; }
        public Action? AfterSend { get; set; }

        public Task SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            if (Failure is not null) throw Failure;
            Sent.Add(message);
            AfterSend?.Invoke();
            return Task.CompletedTask;
        }
    }
}
