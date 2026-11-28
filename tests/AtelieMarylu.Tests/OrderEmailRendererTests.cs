using System.Globalization;
using System.Text.Json;
using AtelieMarylu.Domain.Orders;
using AtelieMarylu.Infrastructure.Data;
using AtelieMarylu.Web.Configuration;
using AtelieMarylu.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AtelieMarylu.Tests;

// Montagem dos e-mails da outbox (AML-0041): idioma gravado na linha, links pelo Store:PublicBaseUrl, sem injeção
// de HTML e o mesmo resultado em toda tentativa (o Resend recusa a mesma chave com conteúdo diferente)
public class OrderEmailRendererTests
{
    private static readonly DateTime At = new(2026, 11, 25, 10, 39, 0, DateTimeKind.Utc);

    private static OrderEmailData Order(string? name = "Maria", bool pickup = false, decimal? amountPaid = null) => new(
        OrderId: 35, OrderCode: "AML-Z89784", CustomerName: name, At: At,
        Items: [new OrderEmailLine("Bolsa de tecido", 2, 180m), new OrderEmailLine("Necessaire", 1, 45m)],
        Subtotal: 225m, ShippingName: pickup ? "Pickup at the studio" : "PAC", ShippingCost: pickup ? 0 : 25m,
        Total: pickup ? 225m : 250m, PixTotal: pickup ? 213.75m : 238.75m, PayBy: At.AddMinutes(30),
        IsPickup: pickup, AddressLine: pickup ? null : "Rua A, 10 · Centro · Rio de Janeiro/RJ · 20000-000", AmountPaid: amountPaid);

    private static CancellationEmailData Cancellation(string? name = "Maria") =>
        new(35, "AML-Z89784", name, "CAN-6AYBHB", At, OrderStatus.Paid);

    private static OutboxEmail Row<T>(string kind, T data, string language = "pt-BR", string to = "cliente@teste.local") => new()
    {
        Kind = kind, Recipient = to, Language = language, OrderId = 35, CreatedAt = At, NextAttemptAt = At,
        Payload = JsonSerializer.Serialize(data, new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } }),
    };

    [Fact]
    public async Task Order_received_lists_items_total_pix_and_deadline()
    {
        var message = await RenderAsync(Row(OutboxEmailKinds.OrderReceived, Order()));

        Assert.Equal("cliente@teste.local", message.To);
        Assert.Equal("Pedido AML-Z89784 recebido", message.Subject);
        Assert.Contains("2 × Bolsa de tecido", message.Html);
        Assert.Contains("R$ 250,00", message.Html);
        Assert.Contains("R$ 238,75", message.Html);       // total no PIX
        Assert.Contains("25/11/2026 08:09", message.Html); // prazo: 11:09 UTC = 08:09 em Brasília
        Assert.Contains("20000-000", message.Html);
        Assert.Contains("https://loja.teste/my-orders/AML-Z89784", message.Html);
    }

    [Fact]
    public async Task Payment_confirmed_shows_the_amount_paid_in_the_customer_language()
    {
        var message = await RenderAsync(Row(OutboxEmailKinds.PaymentConfirmed, Order(amountPaid: 238.75m), language: "es"));

        Assert.Equal("Pago confirmado · Pedido AML-Z89784", message.Subject);
        Assert.Contains("lang=\"es\"", message.Html);
        Assert.Contains("R$ 238,75", message.Html);
    }

    // Invariante de dinheiro: itens + frete − desconto = total pago. Sem a linha do desconto, o e-mail do PIX mostrava
    // itens e frete somando R$ 250,00 e "Total R$ 238,75", e a cobrança parecia errada.
    [Theory]
    [InlineData(238.75, "11,25")]  // PIX com 5% de desconto nos produtos
    [InlineData(200.00, "50,00")]
    public async Task Pix_discount_line_closes_the_sum(decimal paid, string discount)
    {
        var data = Order(amountPaid: paid);

        var message = await RenderAsync(Row(OutboxEmailKinds.PaymentConfirmed, data));

        Assert.Contains("Desconto PIX", message.Html);
        Assert.Contains($"−R$ {discount}", message.Html);
        Assert.Equal(data.Total - paid, decimal.Parse(discount, CultureInfo.GetCultureInfo("pt-BR")));
        Assert.Contains(paid.ToString("C", CultureInfo.GetCultureInfo("pt-BR")), message.Html);
    }

    [Fact]
    public async Task Full_price_payment_has_no_discount_line()
    {
        // Cartão (ou pagamento manual sem desconto): pagou o total cheio
        var message = await RenderAsync(Row(OutboxEmailKinds.PaymentConfirmed, Order(amountPaid: 250m)));

        Assert.DoesNotContain("Desconto PIX", message.Html);
    }

    [Fact]
    public async Task Order_received_shows_the_full_total_without_discount_line()
    {
        // Antes do pagamento ainda não há desconto aplicado: o valor no PIX vai numa frase à parte
        var message = await RenderAsync(Row(OutboxEmailKinds.OrderReceived, Order()));

        Assert.DoesNotContain("Desconto PIX", message.Html);
    }

    [Fact]
    public async Task Pickup_has_no_address()
    {
        var message = await RenderAsync(Row(OutboxEmailKinds.PaymentConfirmed, Order(pickup: true, amountPaid: 213.75m)));

        Assert.DoesNotContain("20000-000", message.Html);
    }

    [Fact]
    public async Task Cancellation_goes_to_customer_in_her_language_and_to_store_in_portuguese()
    {
        var customer = await RenderAsync(Row(OutboxEmailKinds.CancellationReceived, Cancellation(), language: "es"));
        Assert.Equal("Solicitud de cancelación recibida · Pedido AML-Z89784", customer.Subject);
        Assert.Contains("CAN-6AYBHB", customer.Html);
        Assert.Contains("25/11/2026 07:39", customer.Html);

        var store = await RenderAsync(Row(OutboxEmailKinds.CancellationStaffNotice, Cancellation(name: null), to: "contato@loja.teste"));
        Assert.Equal("Solicitação de cancelamento · Pedido AML-Z89784", store.Subject);
        Assert.Contains("lang=\"pt-BR\"", store.Html);
        Assert.Contains("https://loja.teste/admin/orders/35", store.Html);
    }

    [Fact]
    public async Task Customer_name_cannot_inject_html()
    {
        var message = await RenderAsync(Row(OutboxEmailKinds.OrderReceived, Order(name: "<a href=\"https://golpe.teste\">Clique</a>")));

        Assert.DoesNotContain("<a href=\"https://golpe.teste\"", message.Html);
        Assert.Contains("&lt;a href=", message.Html);
    }

    [Fact]
    public async Task Same_row_renders_the_same_email_with_the_same_key()
    {
        var row = Row(OutboxEmailKinds.OrderReceived, Order());

        var first = await RenderAsync(row);
        var second = await RenderAsync(row);

        Assert.Equal($"outbox/{row.Id}", first.IdempotencyKey);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Rendering_does_not_change_the_culture_of_the_caller()
    {
        var before = CultureInfo.CurrentUICulture;

        await RenderAsync(Row(OutboxEmailKinds.OrderReceived, Order(), language: "es"));

        Assert.Equal(before, CultureInfo.CurrentUICulture);
    }

    [Fact]
    public async Task Unknown_kind_fails()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => RenderAsync(Row("unknown", Order())));
    }

    private static async Task<Domain.Email.EmailMessage> RenderAsync(OutboxEmail row)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization(options => options.ResourcesPath = "Resources");
        services.AddSingleton(Options.Create(new ShopOptions { PublicBaseUrl = "https://loja.teste" }));
        services.AddTransient<OrderEmailRenderer>();
        await using var provider = services.BuildServiceProvider();
        return await provider.GetRequiredService<OrderEmailRenderer>().RenderAsync(row);
    }
}

// Store:PublicBaseUrl (AML-0041): a loja não sobe sem um endereço que gere links certos nos e-mails
public class PublicBaseUrlTests
{
    [Theory]
    [InlineData("https://www.ateliemarylu.com.br", true)]
    [InlineData("https://www.ateliemarylu.com.br/", true)]
    [InlineData("", false)]
    [InlineData("www.ateliemarylu.com.br", false)]           // sem https:// não é endereço absoluto
    [InlineData("http://www.ateliemarylu.com.br", false)]     // link de e-mail sem TLS
    [InlineData("https://www.ateliemarylu.com.br/loja", false)] // com caminho, "my-orders/X" perderia o "/loja"
    public void Only_https_root_is_valid(string url, bool expected)
    {
        Assert.Equal(expected, new ShopOptions { PublicBaseUrl = url }.HasValidPublicBaseUrl);
    }

    [Fact]
    public void Links_have_no_double_slash()
    {
        Assert.Equal("https://loja.teste/my-orders/AML-X1", new ShopOptions { PublicBaseUrl = "https://loja.teste/" }.PublicUrl("my-orders/AML-X1"));
    }
}
