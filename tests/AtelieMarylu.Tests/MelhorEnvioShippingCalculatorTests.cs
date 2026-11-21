using System.Net;
using System.Text.Json;
using AtelieMarylu.Domain.Shipping;
using AtelieMarylu.Infrastructure.Integrations;
using AtelieMarylu.Tests.Integration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AtelieMarylu.Tests;

// O formato da resposta segue a documentação do Melhor Envio (cálculo de fretes por produtos).
// Toda falha vira ShippingQuoteException: é a única que o OrderService trata.
public sealed class MelhorEnvioShippingCalculatorTests : IDisposable
{
    private readonly List<HttpClient> clients = [];
    private readonly FakeClock clock = new();
    private readonly List<MelhorEnvioQuoteCache> caches = [];

    private static readonly ShippingPackage[] Packages = [new(350, 10, 25, 30, 2, 89.90m)];

    private const string Quote = """
        [
          { "id": 2, "name": "SEDEX", "price": "41.30", "custom_price": "41.30", "delivery_time": 3, "custom_delivery_time": 4,
            "company": { "id": 1, "name": "Correios" } },
          { "id": 1, "name": "PAC", "price": "23.50", "custom_price": "25.00", "delivery_time": 8, "custom_delivery_time": 9,
            "company": { "id": 1, "name": "Correios" } },
          { "id": 3, "name": ".Package", "error": "Transportadora não atende este trecho.",
            "company": { "id": 2, "name": "Jadlog" } }
        ]
        """;

    [Fact]
    public async Task Sends_origin_destination_products_and_required_headers()
    {
        HttpRequestMessage? sent = null;
        string? body = null;
        var calculator = Calculator(request =>
        {
            sent = request;
            body = request.Content!.ReadAsStringAsync().Result;
            return Json(Quote);
        });

        await calculator.QuoteAsync("01310-100", Packages);

        Assert.Equal(HttpMethod.Post, sent!.Method);
        Assert.Equal("https://sandbox.melhorenvio.test/api/v2/me/shipment/calculate", sent.RequestUri!.ToString());
        Assert.Equal("Bearer", sent.Headers.Authorization!.Scheme);
        Assert.Equal("token-teste", sent.Headers.Authorization.Parameter);
        Assert.Contains("loja@teste.local", sent.Headers.UserAgent.ToString());

        using var json = JsonDocument.Parse(body!);
        var root = json.RootElement;
        Assert.Equal("20000000", root.GetProperty("from").GetProperty("postal_code").GetString());
        Assert.Equal("01310100", root.GetProperty("to").GetProperty("postal_code").GetString());
        var product = root.GetProperty("products")[0];
        Assert.Equal(0.35m, product.GetProperty("weight").GetDecimal());     // gramas viram quilos
        Assert.Equal(89.90m, product.GetProperty("insurance_value").GetDecimal());
        Assert.Equal(2, product.GetProperty("quantity").GetInt32());
        Assert.Equal("1,2", root.GetProperty("services").GetString());
    }

    [Fact]
    public async Task Without_service_list_does_not_send_the_field()
    {
        string? body = null;
        var calculator = Calculator(request => { body = request.Content!.ReadAsStringAsync().Result; return Json("[]"); }, services: "");

        await calculator.QuoteAsync("01310100", Packages);

        Assert.DoesNotContain("services", body);
    }

    [Fact]
    public async Task Uses_custom_price_and_days_skips_errors_and_orders_by_price()
    {
        var options = await Calculator(_ => Json(Quote)).QuoteAsync("01310100", Packages);

        Assert.Collection(options,
            pac =>
            {
                Assert.Equal("me-1", pac.Code);
                Assert.Equal("Correios PAC", pac.Name);
                Assert.Equal(25.00m, pac.Amount);     // custom_price: inclui o que a loja configurar no painel
                Assert.Equal(9, pac.DeliveryDays);    // custom_delivery_time: inclui os dias extras de postagem
            },
            sedex => Assert.Equal("me-2", sedex.Code));
    }

    // Trecho de uma resposta real de produção (21/11/2026, Vila Isabel → Vidigal, RJ): preço como texto,
    // modalidades sem atendimento com price null e "error", e uma modalidade que já traz a transportadora no nome
    private const string RealQuote = """
        [
          { "id": 1, "name": "PAC", "price": null, "custom_price": null, "delivery_time": null, "custom_delivery_time": null,
            "error": "Transportadora não atende este trecho.", "company": { "id": 1, "name": "Correios" } },
          { "id": 2, "name": "SEDEX", "price": "12.03", "custom_price": "12.03", "delivery_time": 2, "custom_delivery_time": 2,
            "error": null, "company": { "id": 1, "name": "Correios" } },
          { "id": 32, "name": "Coleta", "price": "62.75", "custom_price": "62.75", "delivery_time": 5, "custom_delivery_time": 5,
            "error": null, "company": { "id": 14, "name": "Loggi" } },
          { "id": 34, "name": "Loggi Ponto", "price": "63.08", "custom_price": "63.08", "delivery_time": 5, "custom_delivery_time": 5,
            "error": null, "company": { "id": 14, "name": "Loggi" } }
        ]
        """;

    [Fact]
    public async Task Reads_the_real_response_format()
    {
        var options = await Calculator(_ => Json(RealQuote)).QuoteAsync("22452694", Packages);

        Assert.Equal(["Correios SEDEX", "Loggi Coleta", "Loggi Ponto"], options.Select(o => o.Name));
        Assert.Equal(["me-2", "me-32", "me-34"], options.Select(o => o.Code));
        Assert.Equal(12.03m, options[0].Amount);
        Assert.Equal(2, options[0].DeliveryDays);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Zero_or_negative_delivery_time_is_shown_without_a_deadline(int days)
    {
        var body = $$"""[{ "id": 2, "name": "SEDEX", "price": "12.03", "delivery_time": {{days}}, "custom_delivery_time": {{days}}, "company": { "name": "Correios" } }]""";

        var option = Assert.Single(await Calculator(_ => Json(body)).QuoteAsync("01310100", Packages));

        Assert.Null(option.DeliveryDays);
        Assert.Equal(12.03m, option.Amount);
    }

    [Theory]
    [InlineData(true, "https://sandbox.melhorenvio.com.br/painel")]
    [InlineData(false, "https://melhorenvio.com.br/painel")]
    public void Panel_link_follows_the_environment(bool sandbox, string expected)
    {
        Assert.Equal(expected, new MelhorEnvioOptions { Sandbox = sandbox }.PanelUrl.ToString());
    }

    [Fact]
    public async Task No_carrier_serving_the_postal_code_is_an_empty_list_not_a_failure()
    {
        var options = await Calculator(_ => Json("""[{ "id": 1, "name": "PAC", "error": "CEP de destino inválido." }]"""))
            .QuoteAsync("01310100", Packages);

        Assert.Empty(options);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]        // token vencido ou apagado
    [InlineData(HttpStatusCode.UnprocessableEntity)] // dados recusados
    [InlineData(HttpStatusCode.InternalServerError)] // Melhor Envio com problema
    public async Task Refusal_becomes_quote_exception(HttpStatusCode status)
    {
        var calculator = Calculator(_ => new HttpResponseMessage(status) { Content = new StringContent("{\"message\":\"erro\"}") });

        await Assert.ThrowsAsync<ShippingQuoteException>(() => calculator.QuoteAsync("01310100", Packages));
    }

    [Fact]
    public async Task Unexpected_body_becomes_quote_exception()
    {
        // Ex.: um objeto de erro com status 200, no lugar da lista
        var calculator = Calculator(_ => Json("""{ "message": "Unauthenticated." }"""));

        await Assert.ThrowsAsync<ShippingQuoteException>(() => calculator.QuoteAsync("01310100", Packages));
    }

    [Fact]
    public async Task Unreachable_becomes_quote_exception()
    {
        var calculator = Calculator(_ => throw new HttpRequestException("sem conexão"));

        var ex = await Assert.ThrowsAsync<ShippingQuoteException>(() => calculator.QuoteAsync("01310100", Packages));
        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    [Fact]
    public async Task Timeout_becomes_quote_exception()
    {
        var calculator = Calculator(_ => throw new TaskCanceledException("timeout"));

        await Assert.ThrowsAsync<ShippingQuoteException>(() => calculator.QuoteAsync("01310100", Packages));
    }

    [Fact]
    public async Task Cancellation_by_the_caller_is_not_hidden()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var calculator = Calculator(_ => throw new TaskCanceledException("cancelado"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => calculator.QuoteAsync("01310100", Packages, cts.Token));
    }

    [Fact]
    public async Task Same_quote_within_10_minutes_does_not_call_the_api_again()
    {
        var calls = 0;
        var calculator = Calculator(_ => { calls++; return Json(Quote); });

        var first = await calculator.QuoteAsync("01310-100", Packages);
        var second = await calculator.QuoteAsync("01310100", Packages); // mesmo CEP, outro formato

        Assert.Equal(1, calls);
        Assert.Equal(first.Select(o => o.Amount), second.Select(o => o.Amount));
    }

    [Fact]
    public async Task Other_postal_code_or_other_items_are_quoted_again()
    {
        var calls = 0;
        var calculator = Calculator(_ => { calls++; return Json(Quote); });

        await calculator.QuoteAsync("01310100", Packages);
        await calculator.QuoteAsync("22452694", Packages);
        await calculator.QuoteAsync("01310100", [new ShippingPackage(350, 10, 25, 30, 3, 89.90m)]);

        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Cached_quote_expires_after_10_minutes()
    {
        var calls = 0;
        var calculator = Calculator(_ => { calls++; return Json(Quote); });

        await calculator.QuoteAsync("01310100", Packages);
        clock.Now = clock.Now.AddMinutes(11);
        await calculator.QuoteAsync("01310100", Packages);

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Cached_quote_cannot_be_changed_by_whoever_receives_it()
    {
        // A mesma lista vai para todas as clientes: alterar a de uma não pode mudar a das outras
        var calculator = Calculator(_ => Json(Quote));
        await calculator.QuoteAsync("01310100", Packages);

        var cached = (IList<ShippingOption>)await calculator.QuoteAsync("01310100", Packages);

        Assert.Throws<NotSupportedException>(() => cached.Add(new ShippingOption("me-99", "Falsa", 1m, 1)));
        Assert.Throws<NotSupportedException>(() => cached[0] = new ShippingOption("me-99", "Falsa", 1m, 1));
    }

    [Fact]
    public async Task Failure_is_not_cached()
    {
        // Uma queda passageira não pode deixar o CEP "sem frete" por 10 minutos
        var fail = true;
        var calculator = Calculator(_ => fail
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
            : Json(Quote));

        await Assert.ThrowsAsync<ShippingQuoteException>(() => calculator.QuoteAsync("01310100", Packages));
        fail = false;

        Assert.NotEmpty(await calculator.QuoteAsync("01310100", Packages));
    }

    [Fact]
    public async Task Invalid_postal_code_throws_before_calling()
    {
        var called = false;
        var calculator = Calculator(_ => { called = true; return Json(Quote); });

        await Assert.ThrowsAsync<ArgumentException>(() => calculator.QuoteAsync("123", Packages));
        Assert.False(called);
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private MelhorEnvioShippingCalculator Calculator(Func<HttpRequestMessage, HttpResponseMessage> respond, string services = "1,2",
        MelhorEnvioQuoteCache? cache = null)
    {
        var http = new HttpClient(new FakeHandler(respond)) { BaseAddress = new Uri("https://sandbox.melhorenvio.test/") };
        clients.Add(http);
        return new MelhorEnvioShippingCalculator(http,
            Options.Create(new MelhorEnvioOptions
            {
                Token = "token-teste",
                OriginPostalCode = "20000-000",
                ContactEmail = "loja@teste.local",
                Services = services,
            }),
            cache ?? NewCache(),
            NullLogger<MelhorEnvioShippingCalculator>.Instance);
    }

    private MelhorEnvioQuoteCache NewCache()
    {
        var cache = new MelhorEnvioQuoteCache(clock);
        caches.Add(cache);
        return cache;
    }

    public void Dispose()
    {
        foreach (var http in clients)
            http.Dispose();
        foreach (var cache in caches)
            cache.Dispose();
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(respond(request));
    }
}
