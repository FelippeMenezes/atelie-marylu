using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AtelieMarylu.Domain.Payments;
using AtelieMarylu.Infrastructure.Integrations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AtelieMarylu.Tests;

// O PagBank de verdade não é chamado: um HttpMessageHandler falso responde no lugar dele
// e guarda o que a loja enviou, para conferir cabeçalhos e corpo.
public sealed class PagBankPaymentGatewayTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 15, 0, 0, TimeSpan.Zero);
    private const string Reference = "AML-TESTE1-P42";
    private const string CheckoutJson = """{"id":"CHEC_1","links":[{"rel":"SELF","href":"https://x/checkouts/CHEC_1"},{"rel":"PAY","href":"https://pagamento.pagseguro.uol.com.br/pagamento?code=ABC"}]}""";

    private readonly ECDsa pagBankKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly List<HttpClient> clients = [];
    private readonly List<HttpRequestMessage> sent = [];
    private readonly List<string> sentBodies = [];
    private readonly PagBankPublicKeyCache keyCache = new();
    private readonly MutableClock clock = new(Now);

    public void Dispose()
    {
        foreach (var client in clients) client.Dispose();
        pagBankKey.Dispose();
    }

    private static ChargeRequest Charge(PaymentMethod method = PaymentMethod.Pix) =>
        new(OrderId: 10, PaymentId: 42, OrderCode: "AML-TESTE1", Amount: 94.91m, method, "cliente@teste.local", "Maria da Silva",
            "Pedido AML-TESTE1 - Ateliê Marylu", Now.UtcDateTime.AddMinutes(30), InterestFreeInstallments: 3);

    [Fact]
    public async Task Pix_checkout_offers_only_pix_with_the_exact_amount()
    {
        var gateway = Gateway(_ => Json(HttpStatusCode.Created, CheckoutJson));

        var start = await gateway.StartAsync(Charge());

        Assert.Equal(Reference, start.ExternalId);
        Assert.Equal("CHEC_1", start.ProviderReference);
        Assert.Equal("https://pagamento.pagseguro.uol.com.br/pagamento?code=ABC", start.PaymentUrl);
        Assert.Null(start.PixCopyPaste);
        var request = Assert.Single(sent);
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal(Reference, request.Headers.GetValues("x-idempotency-key").Single());

        using var body = JsonDocument.Parse(sentBodies.Single());
        var root = body.RootElement;
        Assert.Equal(9491, root.GetProperty("items")[0].GetProperty("unit_amount").GetInt32());
        Assert.Equal(1, root.GetProperty("items")[0].GetProperty("quantity").GetInt32());
        Assert.Equal("PIX", Assert.Single(root.GetProperty("payment_methods").EnumerateArray()).GetProperty("type").GetString());
        Assert.False(root.TryGetProperty("payment_methods_configs", out _));
        Assert.Equal("https://loja.teste/api/webhooks/payment?ref=AML-TESTE1-P42", root.GetProperty("payment_notification_urls")[0].GetString());
        Assert.Equal("https://loja.teste/my-orders/AML-TESTE1", root.GetProperty("redirect_url").GetString());
    }

    [Fact]
    public async Task Card_checkout_limits_installments_to_interest_free()
    {
        var gateway = Gateway(_ => Json(HttpStatusCode.Created, CheckoutJson));

        await gateway.StartAsync(Charge(PaymentMethod.CreditCard));

        using var body = JsonDocument.Parse(sentBodies.Single());
        var root = body.RootElement;
        Assert.Equal("CREDIT_CARD", root.GetProperty("payment_methods")[0].GetProperty("type").GetString());
        var options = root.GetProperty("payment_methods_configs")[0].GetProperty("config_options").EnumerateArray()
            .ToDictionary(o => o.GetProperty("option").GetString()!, o => o.GetProperty("value").GetString());
        Assert.Equal("3", options["INSTALLMENTS_LIMIT"]);
        Assert.Equal("3", options["INTEREST_FREE_INSTALLMENTS"]);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Refusal_becomes_gateway_exception(HttpStatusCode status)
    {
        var gateway = Gateway(_ => Json(status, """{"error_messages":[]}"""));

        await Assert.ThrowsAsync<PaymentGatewayException>(() => gateway.StartAsync(Charge()));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]                          // viraria XSS no href da página do pedido
    [InlineData("http://pagamento.pagseguro.uol.com.br/x")]      // sem https
    [InlineData("https://pagseguro.uol.com.br.golpe.com/x")]     // domínio parecido
    [InlineData("https://golpe.com/pagbank.com.br")]
    public async Task Suspicious_pay_link_is_refused(string link)
    {
        var gateway = Gateway(_ => Json(HttpStatusCode.Created, $$"""{"id":"CHEC_1","links":[{"rel":"PAY","href":"{{link}}"}]}"""));

        await Assert.ThrowsAsync<PaymentGatewayException>(() => gateway.StartAsync(Charge()));
    }

    [Theory]
    [InlineData("https://pagamento.pagseguro.uol.com.br/pagamento?code=1")]
    [InlineData("https://www.pagbank.com.br/checkout/1")]
    public void PagBank_links_are_accepted(string link)
    {
        Assert.True(PagBankPaymentGateway.IsPagBankPaymentLink(link));
    }

    [Fact]
    public async Task Cancelling_inactivates_the_checkout()
    {
        var gateway = Gateway(_ => new HttpResponseMessage(HttpStatusCode.OK));

        await gateway.CancelChargeAsync("CHEC_1");

        var request = Assert.Single(sent);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.EndsWith("/checkouts/CHEC_1/inactivate", request.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Cancelling_a_checkout_that_no_longer_exists_is_fine()
    {
        await Gateway(_ => Json(HttpStatusCode.NotFound, "{}")).CancelChargeAsync("CHEC_1");
    }

    [Fact]
    public async Task Cancelling_failure_becomes_gateway_exception()
    {
        await Assert.ThrowsAsync<PaymentGatewayException>(() => Gateway(_ => Json(HttpStatusCode.InternalServerError, "{}")).CancelChargeAsync("CHEC_1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("../orders/1")]
    public async Task Cancelling_without_valid_checkout_does_not_call_the_api(string? reference)
    {
        var gateway = Gateway(_ => throw new InvalidOperationException("não deveria chamar"));

        await gateway.CancelChargeAsync(reference);

        Assert.Empty(sent);
    }

    [Fact]
    public async Task Checkout_without_pay_link_becomes_gateway_exception()
    {
        var gateway = Gateway(_ => Json(HttpStatusCode.Created, """{"id":"CHEC_1","links":[]}"""));

        await Assert.ThrowsAsync<PaymentGatewayException>(() => gateway.StartAsync(Charge()));
    }

    [Fact]
    public async Task Unreachable_and_timeout_become_gateway_exception()
    {
        await Assert.ThrowsAsync<PaymentGatewayException>(() => Gateway(_ => throw new HttpRequestException("sem conexão")).StartAsync(Charge()));
        await Assert.ThrowsAsync<PaymentGatewayException>(() => Gateway(_ => throw new TaskCanceledException("timeout")).StartAsync(Charge()));
    }

    [Fact]
    public async Task Forged_notification_never_reaches_the_order_query()
    {
        using var attacker = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var gateway = Gateway(PagBankApi(Order("PAID", 9491)));

        await Assert.ThrowsAsync<InvalidPaymentNotificationException>(() => gateway.ReadNotificationAsync(Webhook(signWith: attacker)));

        Assert.DoesNotContain(sent, r => r.RequestUri!.AbsolutePath.Contains("/orders/"));
    }

    [Fact]
    public async Task Status_and_amount_come_from_the_api_not_from_the_body()
    {
        var gateway = Gateway(PagBankApi(Order("PAID", 9491)));

        // O corpo diz outra coisa: tem de ser ignorado, menos o id do pedido
        var notification = await gateway.ReadNotificationAsync(Webhook(body: """{"id":"ORDE_ABC-1","items":[{"reference_id":"AML-TESTE1-P42"}],"charges":[{"status":"DECLINED","amount":{"value":1}}]}"""));

        Assert.NotNull(notification);
        Assert.Equal(Reference, notification.ExternalId);
        Assert.Equal(PaymentStatus.Approved, notification.Status);
        Assert.Equal(94.91m, notification.AmountPaid);
        Assert.Equal("ORDE_ABC-1:PAID", notification.EventId);
        Assert.Contains(sent, r => r.RequestUri!.AbsolutePath.EndsWith("/orders/ORDE_ABC-1"));
    }

    [Fact]
    public async Task Public_key_is_fetched_once_and_reused()
    {
        var gateway = Gateway(PagBankApi(Order("PAID", 9491)));

        await gateway.ReadNotificationAsync(Webhook());
        await gateway.ReadNotificationAsync(Webhook());

        Assert.Single(sent, r => r.RequestUri!.AbsolutePath.EndsWith("/public-keys/webhook"));
    }

    [Fact]
    public async Task New_key_from_pagbank_is_picked_up()
    {
        // A loja guardou a chave antiga; o PagBank trocou de chave e já assina com a nova
        using var oldKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var current = oldKey;
        var gateway = Gateway(request => request.RequestUri!.AbsolutePath.EndsWith("/public-keys/webhook")
            ? Json(HttpStatusCode.OK, $$"""{"public_key":"{{Convert.ToBase64String(current.ExportSubjectPublicKeyInfo())}}"}""")
            : Json(HttpStatusCode.OK, Order("PAID", 9491)));
        await Assert.ThrowsAsync<InvalidPaymentNotificationException>(() => gateway.ReadNotificationAsync(Webhook(signWith: pagBankKey)));

        current = pagBankKey;
        clock.Advance(PagBankPublicKeyCache.MinimumRefreshInterval + TimeSpan.FromSeconds(1));

        Assert.NotNull(await gateway.ReadNotificationAsync(Webhook(signWith: pagBankKey)));
    }

    [Theory]
    [InlineData("""{"id":"../users/1"}""")]
    [InlineData("""{"id":123}""")]
    [InlineData("não é json")]
    public async Task Strange_order_id_is_ignored_without_querying(string body)
    {
        var gateway = Gateway(PagBankApi(Order("PAID", 9491)));

        Assert.Null(await gateway.ReadNotificationAsync(Webhook(body: body)));
        Assert.DoesNotContain(sent, r => r.RequestUri!.AbsolutePath.Contains("/orders/"));
    }

    [Fact]
    public async Task Missing_reference_is_ignored()
    {
        var gateway = Gateway(PagBankApi(Order("PAID", 9491)));

        Assert.Null(await gateway.ReadNotificationAsync(Webhook(reference: null)));
    }

    [Fact]
    public async Task Paid_charge_wins_over_a_declined_one()
    {
        // Primeiro cartão recusado, depois outro aprovado, no mesmo pedido do PagBank
        var order = """{"id":"ORDE_ABC-1","items":[{"reference_id":"AML-TESTE1-P42"}],"charges":[{"status":"DECLINED","amount":{"value":9491}},{"status":"PAID","amount":{"value":9491}}]}""";
        var gateway = Gateway(PagBankApi(order));

        var notification = await gateway.ReadNotificationAsync(Webhook());

        Assert.Equal(PaymentStatus.Approved, notification?.Status);
    }

    [Fact]
    public async Task Query_reads_every_order_of_the_checkout_and_returns_the_paid_one()
    {
        // Resposta real do sandbox (12/11/2026): o checkout lista os pedidos em orders[]
        var gateway = Gateway(request => request.RequestUri!.AbsolutePath switch
        {
            var path when path.EndsWith("/checkouts/CHEC_1") => Json(HttpStatusCode.OK,
                """{"id":"CHEC_1","status":"ACTIVE","orders":[{"id":"ORDE_A"},{"id":"ORDE_B"}]}"""),
            var path when path.EndsWith("/orders/ORDE_A") => Json(HttpStatusCode.OK,
                """{"id":"ORDE_A","items":[{"reference_id":"AML-TESTE1-P42"}],"charges":[{"status":"DECLINED","amount":{"value":9491}}]}"""),
            _ => Json(HttpStatusCode.OK,
                """{"id":"ORDE_B","items":[{"reference_id":"AML-TESTE1-P42"}],"charges":[{"status":"PAID","amount":{"value":9491}}]}"""),
        });

        var status = await gateway.QueryStatusAsync(Reference, "CHEC_1");

        Assert.Equal(PaymentStatus.Approved, status?.Status);
        Assert.Equal("ORDE_B:PAID", status?.EventId);
    }

    [Fact]
    public async Task Query_of_checkout_without_payment_returns_null()
    {
        var gateway = Gateway(_ => Json(HttpStatusCode.OK, """{"id":"CHEC_1","status":"ACTIVE"}"""));

        Assert.Null(await gateway.QueryStatusAsync(Reference, "CHEC_1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("../orders/1")]
    public async Task Query_without_valid_checkout_does_not_call_the_api(string? checkout)
    {
        var gateway = Gateway(_ => throw new InvalidOperationException("não deveria chamar"));

        Assert.Null(await gateway.QueryStatusAsync(Reference, checkout));
        Assert.Empty(sent);
    }

    [Fact]
    public async Task Unsigned_notice_is_accepted_when_the_store_account_confirms_it()
    {
        // Os avisos do checkout chegam sem assinatura (sandbox, 12/11/2026): quem decide é a consulta à API
        var gateway = Gateway(PagBankApi(Order("PAID", 9491)));

        var notification = await gateway.ReadNotificationAsync(Webhook(unsigned: true));

        Assert.Equal(PaymentStatus.Approved, notification?.Status);
        Assert.DoesNotContain(sent, r => r.RequestUri!.AbsolutePath.EndsWith("/public-keys/webhook"));
    }

    [Fact]
    public async Task Unsigned_notice_of_an_order_outside_the_store_account_is_ignored()
    {
        // Pedido de outra conta do PagBank: a consulta com o token da loja não o encontra
        var gateway = Gateway(_ => Json(HttpStatusCode.NotFound, "{}"));

        Assert.Null(await gateway.ReadNotificationAsync(Webhook(unsigned: true)));
    }

    [Fact]
    public async Task Order_without_our_reference_is_ignored()
    {
        // Sem a referência no pedido, não há como provar a quem o pagamento pertence
        var gateway = Gateway(PagBankApi("""{"id":"ORDE_ABC-1","charges":[{"status":"PAID","amount":{"value":9491}}]}"""));

        Assert.Null(await gateway.ReadNotificationAsync(Webhook(unsigned: true)));
    }

    [Fact]
    public async Task Order_of_another_reference_is_ignored()
    {
        // Aviso verdadeiro do PagBank, mas o pedido dele pertence a outra cobrança da loja
        var order = """{"id":"ORDE_ABC-1","items":[{"reference_id":"AML-OUTRO-P7"}],"charges":[{"status":"PAID","amount":{"value":9491}}]}""";
        var gateway = Gateway(PagBankApi(order));

        Assert.Null(await gateway.ReadNotificationAsync(Webhook()));
    }

    [Fact]
    public async Task Order_of_the_same_reference_is_accepted()
    {
        var order = """{"id":"ORDE_ABC-1","items":[{"reference_id":"AML-TESTE1-P42"}],"charges":[{"status":"PAID","amount":{"value":9491}}]}""";
        var gateway = Gateway(PagBankApi(order));

        Assert.Equal(PaymentStatus.Approved, (await gateway.ReadNotificationAsync(Webhook()))?.Status);
    }

    [Fact]
    public async Task Amount_comes_from_what_was_actually_paid()
    {
        var order = """{"id":"ORDE_ABC-1","items":[{"reference_id":"AML-TESTE1-P42"}],"charges":[{"status":"PAID","amount":{"value":9491,"summary":{"total":9491,"paid":9000,"refunded":0}}}]}""";
        var gateway = Gateway(PagBankApi(order));

        Assert.Equal(90.00m, (await gateway.ReadNotificationAsync(Webhook()))?.AmountPaid);
    }

    [Fact]
    public async Task Full_refund_becomes_refunded()
    {
        var order = """{"id":"ORDE_ABC-1","items":[{"reference_id":"AML-TESTE1-P42"}],"charges":[{"status":"PAID","amount":{"value":9491,"summary":{"total":9491,"paid":9491,"refunded":9491}}}]}""";
        var gateway = Gateway(PagBankApi(order));

        var notification = await gateway.ReadNotificationAsync(Webhook());

        Assert.Equal(PaymentStatus.Refunded, notification?.Status);
        Assert.Equal("ORDE_ABC-1:REFUNDED", notification?.EventId);
    }

    [Fact]
    public async Task Full_refund_counts_even_when_the_charge_shows_canceled()
    {
        // A devolução é pedida em /charges/{id}/cancel: a cobrança pode passar a aparecer como CANCELED
        var order = """{"id":"ORDE_ABC-1","items":[{"reference_id":"AML-TESTE1-P42"}],"charges":[{"status":"CANCELED","amount":{"value":9491,"summary":{"total":9491,"paid":9491,"refunded":9491}}}]}""";
        var gateway = Gateway(PagBankApi(order));

        Assert.Equal(PaymentStatus.Refunded, (await gateway.ReadNotificationAsync(Webhook()))?.Status);
    }

    [Fact]
    public async Task Partial_refund_does_not_change_the_order()
    {
        // Devolução parcial é decisão da loja (ex.: faltou uma peça): o pagamento continua aprovado
        var order = """{"id":"ORDE_ABC-1","items":[{"reference_id":"AML-TESTE1-P42"}],"charges":[{"status":"PAID","amount":{"value":9491,"summary":{"total":9491,"paid":9491,"refunded":2000}}}]}""";
        var gateway = Gateway(PagBankApi(order));

        var notification = await gateway.ReadNotificationAsync(Webhook());

        Assert.Equal(PaymentStatus.Approved, notification?.Status);
        Assert.Equal("ORDE_ABC-1:PAID", notification?.EventId);
    }

    [Theory]
    [InlineData("WAITING")]
    [InlineData("IN_ANALYSIS")]
    [InlineData("AUTHORIZED")]
    public void Intermediate_statuses_change_nothing(string pagBank)
    {
        // Um aviso intermediário atrasado não pode reabrir um pagamento que a loja já cancelou
        Assert.Null(PagBankPaymentGateway.MapChargeStatus(pagBank));
    }

    [Theory]
    [InlineData("PAID", PaymentStatus.Approved)]
    [InlineData("DECLINED", PaymentStatus.Rejected)]
    [InlineData("CANCELED", PaymentStatus.Cancelled)]
    public void Clear_statuses_are_mapped(string pagBank, PaymentStatus expected)
    {
        Assert.Equal(expected, PagBankPaymentGateway.MapChargeStatus(pagBank));
    }

    [Fact]
    public void Unknown_status_is_ignored()
    {
        // Um status novo que o PagBank venha a criar não pode derrubar o webhook
        Assert.Null(PagBankPaymentGateway.MapChargeStatus("ALGO_NOVO"));
    }

    private static string Order(string status, int cents) =>
        $$$"""{"id":"ORDE_ABC-1","items":[{"reference_id":"AML-TESTE1-P42"}],"charges":[{"status":"{{{status}}}","amount":{"value":{{{cents}}}}}]}""";

    private Func<HttpRequestMessage, HttpResponseMessage> PagBankApi(string orderJson) => request =>
        request.RequestUri!.AbsolutePath.EndsWith("/public-keys/webhook")
            ? Json(HttpStatusCode.OK, $$"""{"public_key":"{{Convert.ToBase64String(pagBankKey.ExportSubjectPublicKeyInfo())}}"}""")
            : Json(HttpStatusCode.OK, orderJson);

    private PaymentWebhookRequest Webhook(string body = """{"id":"ORDE_ABC-1"}""", ECDsa? signWith = null, string? reference = Reference, bool unsigned = false)
    {
        var raw = Encoding.UTF8.GetBytes(body);
        var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (reference is not null) query["ref"] = reference;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!unsigned) headers["x-payload-signature"] = PagBankSignatureTests.Sign(signWith ?? pagBankKey, raw);
        return new PaymentWebhookRequest(raw, headers, query);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private PagBankPaymentGateway Gateway(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var http = new HttpClient(new FakeHandler(request =>
        {
            sent.Add(request);
            if (request.Content is not null) sentBodies.Add(request.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            return respond(request);
        }))
        { BaseAddress = new Uri("https://sandbox.api.pagseguro.com/") };
        clients.Add(http);
        var options = Options.Create(new PagBankOptions { Token = "token-de-teste", PublicBaseUrl = "https://loja.teste/" });
        return new PagBankPaymentGateway(http, options, keyCache, clock, NullLogger<PagBankPaymentGateway>.Instance);
    }

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;
        public void Advance(TimeSpan by) => current += by;
        public override DateTimeOffset GetUtcNow() => current;
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(respond(request));
    }
}
