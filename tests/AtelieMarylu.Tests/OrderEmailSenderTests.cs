using System.Globalization;
using AtelieMarylu.Domain.Email;
using AtelieMarylu.Domain.Orders;
using AtelieMarylu.Web.Configuration;
using AtelieMarylu.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AtelieMarylu.Tests;

// E-mails do cancelamento pedido pela cliente (AML-0040): quem recebe, em que idioma e sem injeção de HTML
public class OrderEmailSenderTests
{
    private static CancellationOutcome Request(string? reason = "Comprei a cor errada") => new(
        OrderId: 35, OrderCode: "AML-Z89784", CustomerEmail: "cliente@teste.local", CustomerName: "Maria",
        CancelledNow: false, Protocol: "CAN-6AYBHB", At: new DateTime(2026, 11, 25, 10, 39, 0, DateTimeKind.Utc),
        OrderStatusAtRequest: OrderStatus.Paid, Reason: reason);

    [Fact]
    public async Task Unpaid_order_cancelled_now_sends_no_email()
    {
        var client = await SendAsync(Request() with { CancelledNow = true, Protocol = null });

        Assert.Empty(client.Sent);
    }

    [Fact]
    public async Task Customer_gets_her_language_and_store_gets_portuguese()
    {
        var client = await SendAsync(Request(), culture: "es");

        Assert.Equal(2, client.Sent.Count);
        var customer = client.Sent.Single(m => m.To == "cliente@teste.local");
        Assert.Equal("Solicitud de cancelación recibida · Pedido AML-Z89784", customer.Subject);
        Assert.Contains("CAN-6AYBHB", customer.Html);
        Assert.Contains("https://loja.teste/my-orders/AML-Z89784", customer.Html);
        // 10:39 UTC = 07:39 em Brasília
        Assert.Contains("25/11/2026 07:39", customer.Html);

        var store = client.Sent.Single(m => m.To == "contato@loja.teste");
        Assert.Equal("Solicitação de cancelamento · Pedido AML-Z89784", store.Subject);
        Assert.Contains("lang=\"pt-BR\"", store.Html);
        Assert.Contains("https://loja.teste/admin/orders/35", store.Html);
    }

    [Fact]
    public async Task Without_store_email_only_the_customer_is_notified()
    {
        var client = await SendAsync(Request(), contactEmail: "");

        Assert.Equal("cliente@teste.local", Assert.Single(client.Sent).To);
    }

    [Fact]
    public async Task Reason_written_by_the_customer_cannot_inject_html()
    {
        var client = await SendAsync(Request(reason: "<a href=\"https://golpe.teste\">Clique</a>"));

        var store = client.Sent.Single(m => m.To == "contato@loja.teste");
        Assert.DoesNotContain("<a href=\"https://golpe.teste\"", store.Html);
        Assert.Contains("&lt;a href=", store.Html);
    }

    [Fact]
    public async Task Delivery_failure_stays_in_the_log()
    {
        // A solicitação já está gravada: falhar o e-mail não pode virar erro na tela
        var client = await SendAsync(Request(), failing: true);

        Assert.Empty(client.Sent);
    }

    private static async Task<FakeEmailClient> SendAsync(CancellationOutcome outcome, string culture = "pt-BR",
        string contactEmail = "contato@loja.teste", bool failing = false)
    {
        var client = new FakeEmailClient(failing);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization(options => options.ResourcesPath = "Resources");
        services.AddSingleton<IEmailClient>(client);
        services.AddSingleton<NavigationManager>(new TestNavigation());
        services.AddSingleton(Options.Create(new ShopOptions { ContactEmail = contactEmail }));
        services.AddTransient<OrderEmailSender>();
        await using var provider = services.BuildServiceProvider();

        var (previous, previousUi) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            await provider.GetRequiredService<OrderEmailSender>().TrySendCancellationEmailsAsync(outcome);
            // O aviso da loja troca o idioma só enquanto monta o e-mail: o da cliente volta depois
            Assert.Equal(culture, CultureInfo.CurrentUICulture.Name);
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = (previous, previousUi);
        }
        return client;
    }

    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("https://loja.teste/", "https://loja.teste/");
    }

    private sealed class FakeEmailClient(bool failing) : IEmailClient
    {
        public List<EmailMessage> Sent { get; } = [];

        public Task SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            if (failing) throw new EmailDeliveryException("Resend fora do ar (teste)");
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }
}
