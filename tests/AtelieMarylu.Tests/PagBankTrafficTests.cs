using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using AtelieMarylu.Domain.Payments;
using AtelieMarylu.Infrastructure.Integrations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AtelieMarylu.Tests;

// O registro de chamadas para a homologação: grava o que o PagBank precisa ver, e NUNCA o token
// nem os dados pessoais da cliente (LGPD).
public sealed class PagBankTrafficTests : IDisposable
{
    // Cada teste grava numa pasta própria, apagada no fim
    private readonly string folder = Path.Combine(Path.GetTempPath(), "pagbank-traffic-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    // Resposta real do PagBank, encurtada, com dados pessoais inventados
    private const string OrderWithCustomer = """
        {"id":"ORDE_1","customer":{"name":"Maria Teste","email":"maria@teste.local","tax_id":"12345678909","phones":[{"area":21,"number":999999999}]},
         "items":[{"reference_id":"AML-TESTE1-P42","name":"Pedido AML-TESTE1","unit_amount":9491}],
         "charges":[{"id":"CHAR_1","status":"PAID","amount":{"value":9491},"payment_method":{"type":"CREDIT_CARD","card":{"holder":{"name":"MARIA TESTE"},"last_digits":"1111"}}}]}
        """;

    private PagBankTrafficRecorder Recorder(bool enabled) =>
        new(Options.Create(new PagBankOptions { LogTraffic = enabled, TrafficLogDirectory = folder, Token = "token-secreto", PublicBaseUrl = "https://loja.teste" }),
            TimeProvider.System, NullLogger<PagBankTrafficRecorder>.Instance);

    private string[] Files() => Directory.Exists(folder) ? Directory.GetFiles(folder) : [];

    [Fact]
    public void Personal_data_is_masked_and_the_rest_is_kept()
    {
        var masked = PagBankTrafficRecorder.MaskBody(OrderWithCustomer)!.ToJsonString();

        Assert.DoesNotContain("Maria", masked, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("maria@teste.local", masked);
        Assert.DoesNotContain("12345678909", masked);
        Assert.DoesNotContain("999999999", masked);
        Assert.DoesNotContain("1111", masked);
        // O que a homologação precisa ver continua lá
        Assert.Contains("AML-TESTE1-P42", masked);
        Assert.Contains("PAID", masked);
        Assert.Contains("9491", masked);
    }

    [Fact]
    public void Body_that_is_not_json_is_not_recorded()
    {
        var masked = PagBankTrafficRecorder.MaskBody("nome=Maria&cpf=12345678909")!.ToJsonString();

        Assert.DoesNotContain("12345678909", masked);
    }

    [Fact]
    public async Task Handler_records_request_and_response_without_token_or_personal_data()
    {
        var recorder = Recorder(enabled: true);
        using var http = new HttpClient(new PagBankTrafficHandler(recorder)
        {
            InnerHandler = new FixedResponse(OrderWithCustomer),
        })
        { BaseAddress = new Uri("https://sandbox.api.pagseguro.com/") };
        using var request = new HttpRequestMessage(HttpMethod.Get, "orders/ORDE_1");
        request.Headers.Authorization = new("Bearer", "token-secreto");

        using var response = await http.SendAsync(request);

        // O gateway ainda consegue ler a resposta depois do registro
        Assert.Contains("ORDE_1", await response.Content.ReadAsStringAsync());
        var file = Assert.Single(Files());
        Assert.Contains("consultar-pedido", Path.GetFileName(file));
        var content = await File.ReadAllTextAsync(file);
        Assert.DoesNotContain("token-secreto", content);
        Assert.Contains("Bearer ***", content);
        Assert.DoesNotContain("maria@teste.local", content);
        var entry = JsonNode.Parse(content)!;
        Assert.Equal("sandbox", entry["environment"]!.GetValue<string>());
        Assert.Equal(200, entry["response"]!["status"]!.GetValue<int>());
    }

    [Fact]
    public async Task Nothing_is_recorded_when_switched_off()
    {
        using var http = new HttpClient(new PagBankTrafficHandler(Recorder(enabled: false)) { InnerHandler = new FixedResponse("{}") });

        using var response = await http.GetAsync("https://sandbox.api.pagseguro.com/orders/ORDE_1");

        Assert.Empty(Files());
    }

    [Theory]
    [InlineData("POST", "checkouts", "criar-checkout")]
    [InlineData("GET", "checkouts/CHEC_1", "consultar-checkout")]
    [InlineData("POST", "checkouts/CHEC_1/inactivate", "inativar-checkout")]
    [InlineData("GET", "orders/ORDE_1", "consultar-pedido")]
    [InlineData("GET", "public-keys/webhook", "consultar-chave-publica")]
    public void Files_are_named_after_the_operation(string method, string path, string expected)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), "https://sandbox.api.pagseguro.com/" + path);

        Assert.Equal(expected, PagBankTrafficHandler.OperationName(request));
    }

    [Fact]
    public async Task Received_notice_is_recorded_masked()
    {
        var recorder = Recorder(enabled: true);
        using var http = new HttpClient(new FixedResponse("""{"id":"ORDE_1","items":[{"reference_id":"AML-TESTE1-P42"}],"charges":[{"status":"WAITING"}]}"""))
        { BaseAddress = new Uri("https://sandbox.api.pagseguro.com/") };
        var options = Options.Create(new PagBankOptions { Token = "token-secreto", PublicBaseUrl = "https://loja.teste" });
        var gateway = new PagBankPaymentGateway(http, options, new PagBankPublicKeyCache(), recorder, TimeProvider.System, NullLogger<PagBankPaymentGateway>.Instance);
        var body = Encoding.UTF8.GetBytes(OrderWithCustomer);

        await gateway.ReadNotificationAsync(new PaymentWebhookRequest(body,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["ref"] = "AML-TESTE1-P42" }));

        var file = Assert.Single(Files());
        Assert.Contains("aviso-recebido", Path.GetFileName(file));
        var content = await File.ReadAllTextAsync(file);
        Assert.DoesNotContain("maria@teste.local", content);
        Assert.Contains("AML-TESTE1-P42", content);
    }

    private sealed class FixedResponse(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }
}
