using System.Net;
using System.Text;
using AtelieMarylu.Domain.Carts;
using AtelieMarylu.Domain.Orders;
using AtelieMarylu.Domain.Payments;
using AtelieMarylu.Domain.Shipping;
using AtelieMarylu.Infrastructure.Integrations;
using AtelieMarylu.Web;
using AtelieMarylu.Web.Configuration;
using AtelieMarylu.Web.Endpoints;
using AtelieMarylu.Web.Extensions;
using AtelieMarylu.Web.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AtelieMarylu.Tests.Integration;

// O webhook chamado por HTTP de verdade, num servidor em memória. Pega o que os testes de classe não pegam:
// códigos de resposta, limite de tamanho, limite de chamadas e a reexecução de erros como página (que, na loja,
// transformava 401 e 503 em 400; o problema só tinha aparecido no sandbox).
public class PaymentWebhookEndpointTests(TestDatabase database) : IClassFixture<TestDatabase>
{
    private const string Url = "/api/webhooks/payment";
    private readonly RecordingPaymentGateway gateway = new();
    private readonly FakeClock clock = new();
    private readonly ShopOptions store = new() { PaymentDeadlineMinutes = 120, MaxPendingOrdersPerCustomer = 20 };

    private OrderService Service()
    {
        var audit = new AuditService(new CurrentUserService(new ServiceCollection().BuildServiceProvider(), new HttpContextAccessor()), database);
        var localizer = new ServiceCollection().AddLogging().AddLocalization(o => o.ResourcesPath = "Resources").BuildServiceProvider()
            .GetRequiredService<IStringLocalizer<SharedResource>>();
        return new OrderService(database, new SimulatedShippingCalculator(), gateway, new InlineChargeCloser(gateway), audit, new OrderEvents(),
            Options.Create(store), clock, localizer, NullLogger<OrderService>.Instance);
    }

    // Monta só o necessário, na mesma ordem da loja: reexecução de erros como página, limite de chamadas e o webhook
    private async Task<WebApplication> StartAsync(string environment = "Production")
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddRouting();
        builder.Services.AddStoreRateLimiting();
        builder.Services.AddSingleton<IPaymentGateway>(gateway);
        builder.Services.AddScoped(_ => Service());

        var app = builder.Build();
        app.UseStatusCodePagesWithReExecute("/not-found");
        app.UseRateLimiter();
        // Como a página "não encontrada" da loja: recusa o POST reexecutado (na loja, por falta de antiforgery)
        app.Map("/not-found", () => Results.BadRequest());
        app.MapPaymentWebhooks();
        await app.StartAsync();
        return app;
    }

    private async Task<Payment> PendingPaymentAsync()
    {
        var customer = await database.CreateCustomerAsync();
        var product = await database.CreateProductAsync(stock: 5);
        var service = Service();
        var order = await service.CreateAsync(customer.Id, [new CartItem(product.Id, 1)], new ShippingAddress
        {
            PostalCode = "20000-000", Street = "Rua A", Number = "1", District = "Centro", City = "Rio de Janeiro", State = "RJ",
        }, "pac");
        return await service.StartPaymentAsync(order.Id, PaymentMethod.Pix);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string? reference, string body = "{}")
    {
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        return await client.PostAsync(reference is null ? Url : $"{Url}?ref={reference}", content);
    }

    [FactWithDatabase]
    public async Task Unknown_reference_never_reaches_the_provider()
    {
        await using var app = await StartAsync();

        var response = await PostAsync(app.GetTestClient(), "AML-NAOEXISTE-P1");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, gateway.NotificationsRead);
    }

    [FactWithDatabase]
    public async Task Forged_notice_gets_401_and_not_400()
    {
        var payment = await PendingPaymentAsync();
        gateway.OnNotification = _ => throw new InvalidPaymentNotificationException("assinatura inválida");
        await using var app = await StartAsync();

        var response = await PostAsync(app.GetTestClient(), payment.ExternalId);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [FactWithDatabase]
    public async Task Provider_down_gets_503_so_it_sends_again()
    {
        var payment = await PendingPaymentAsync();
        gateway.OnNotification = _ => throw new PaymentGatewayException("fora do ar");
        await using var app = await StartAsync();

        var response = await PostAsync(app.GetTestClient(), payment.ExternalId);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [FactWithDatabase]
    public async Task Notice_that_changes_nothing_gets_200_so_it_is_not_sent_again()
    {
        var payment = await PendingPaymentAsync();
        await using var app = await StartAsync();

        var response = await PostAsync(app.GetTestClient(), payment.ExternalId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, gateway.NotificationsRead);
    }

    [FactWithDatabase]
    public async Task Approved_notice_marks_the_order_paid()
    {
        var payment = await PendingPaymentAsync();
        gateway.OnNotification = _ => new PaymentNotification($"{payment.ExternalId}:PAID", payment.ExternalId!, PaymentStatus.Approved, payment.Amount);
        await using var app = await StartAsync();

        var response = await PostAsync(app.GetTestClient(), payment.ExternalId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(OrderStatus.Paid, (await Service().GetAsync(payment.OrderId))!.Status);
    }

    [FactWithDatabase]
    public async Task Too_large_body_gets_413()
    {
        await using var app = await StartAsync();

        var response = await PostAsync(app.GetTestClient(), "AML-X-P1", new string('a', 17 * 1024));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [FactWithDatabase]
    public async Task Gateway_that_accepts_anything_is_closed_outside_development()
    {
        // O simulado aceita qualquer aviso: em produção o webhook dele não pode existir
        var payment = await PendingPaymentAsync();
        gateway.VerifiesNotifications = false;
        await using var app = await StartAsync("Production");

        var response = await PostAsync(app.GetTestClient(), payment.ExternalId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, gateway.NotificationsRead);
    }

    [FactWithDatabase]
    public async Task Webhook_has_its_own_rate_limit()
    {
        await using var app = await StartAsync();
        var client = app.GetTestClient();

        for (var i = 0; i < 120; i++)
            Assert.NotEqual(HttpStatusCode.TooManyRequests, (await PostAsync(client, "AML-X-P1")).StatusCode);

        var blocked = await PostAsync(client, "AML-X-P1");
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.NotNull(blocked.Headers.RetryAfter);
    }

    [FactWithDatabase]
    public async Task Login_limit_is_separate_from_the_webhook_limit()
    {
        // Um pico de avisos do provedor não pode bloquear o login, nem o contrário
        await using var app = await StartAsync();
        var client = app.GetTestClient();
        for (var i = 0; i < 120; i++)
            await PostAsync(client, "AML-X-P1");

        using var content = new StringContent("");
        var login = await client.PostAsync("/Account/Login", content);

        Assert.NotEqual(HttpStatusCode.TooManyRequests, login.StatusCode);
    }
}
