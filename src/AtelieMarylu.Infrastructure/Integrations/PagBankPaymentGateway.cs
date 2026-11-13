using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AtelieMarylu.Domain.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AtelieMarylu.Infrastructure.Integrations;

// Pagamento real pelo Checkout PagBank (página de pagamento hospedada pelo PagBank).
// A loja cria um checkout com o valor e manda a cliente para o link "PAY". O CPF e os dados do cartão
// são digitados no PagBank e nunca passam pela loja. Mesmo padrão do Resend: HttpClient + Options, sem SDK.
//
// Como o aviso de pagamento chega: um POST com um "pedido" do PagBank (ORDE_...). A documentação não diz
// como ligar esse pedido ao checkout que criamos. Por isso a própria loja põe a referência do pagamento
// na URL de aviso de cada checkout (?ref=AML-XXXXXX-P42), e o aviso volta com ela.
//
// Autenticidade do aviso (confirmado no sandbox em 12/11/2026): os avisos do CHECKOUT chegam SEM assinatura.
// Por isso quem decide é a consulta GET /orders/{id}, feita com o token da própria loja: ela só enxerga pedidos
// da conta da loja. O aviso só é aceito se esse pedido existir lá, pertencer à nossa referência (items[].reference_id)
// e, na confirmação, tiver o valor exato. Um aviso forjado não consegue cumprir isso sem um pagamento real.
// Quando o aviso vier assinado (x-payload-signature), a assinatura também é conferida: inválida = recusado.
// O status e o valor vêm SEMPRE da consulta, nunca do corpo recebido.
public partial class PagBankPaymentGateway(
    HttpClient http,
    IOptions<PagBankOptions> options,
    PagBankPublicKeyCache keyCache,
    TimeProvider clock,
    ILogger<PagBankPaymentGateway> logger) : IPaymentGateway
{
    public const string ProviderName = "pagbank";

    public string Name => ProviderName;

    // Pela consulta ao PagBank com o token da loja (e pela assinatura, quando ela vem). Veja o comentário da classe.
    public bool VerifiesNotifications => true;

    // Referência que a loja dá a cada cobrança: código do pedido + Id do nosso Payment
    public static string ReferenceFor(string orderCode, int paymentId) => $"{orderCode}-P{paymentId}";

    public async Task<PaymentStart> StartAsync(ChargeRequest charge, CancellationToken ct = default)
    {
        var settings = options.Value;
        var reference = ReferenceFor(charge.OrderCode, charge.PaymentId);
        var baseUrl = settings.PublicBaseUrl.TrimEnd('/');

        // Um item só, com o total já calculado pela loja (frete e desconto do PIX incluídos).
        // Assim o valor cobrado é exatamente o do nosso Payment, e a conferência do webhook fica simples.
        var body = new CreateCheckout(
            ReferenceId: reference,
            ExpirationDate: new DateTimeOffset(charge.ExpiresAt, TimeSpan.Zero).ToString("yyyy-MM-dd'T'HH:mm:ssK", CultureInfo.InvariantCulture),
            Items: [new CheckoutItem(reference, Truncate(charge.Description, 100), 1, ToCents(charge.Amount))],
            // A forma de pagamento já foi escolhida na loja, porque o PIX tem desconto: o PagBank oferece só ela
            PaymentMethods: [new PaymentMethodType(charge.Method == PaymentMethod.Pix ? "PIX" : "CREDIT_CARD")],
            PaymentMethodsConfigs: charge.Method == PaymentMethod.Pix ? null : [InstallmentsConfig(charge.InterestFreeInstallments)],
            SoftDescriptor: "ATELIE MARYLU",
            RedirectUrl: $"{baseUrl}/my-orders/{Uri.EscapeDataString(charge.OrderCode)}",
            ReturnUrl: $"{baseUrl}/my-orders/{Uri.EscapeDataString(charge.OrderCode)}",
            PaymentNotificationUrls: [$"{baseUrl}/api/webhooks/payment?ref={Uri.EscapeDataString(reference)}"]);

        using var request = new HttpRequestMessage(HttpMethod.Post, "checkouts") { Content = JsonContent.Create(body) };
        // Idempotência: a documentação do PagBank cita uma chave enviada por cabeçalho, mas não diz o nome.
        // "x-idempotency-key" é o nome usual; se o PagBank o ignorar, nada quebra. Conferir no sandbox.
        request.Headers.Add("x-idempotency-key", reference);

        var created = await SendAsync<CheckoutResponse>(request, "criar o checkout", ct)
            ?? throw new PaymentGatewayException("O PagBank não devolveu o checkout criado.");
        if (created.Id is null || !CheckoutIdFormat().IsMatch(created.Id))
            throw new PaymentGatewayException("O PagBank devolveu um checkout com id inesperado.");

        // O link vai parar num href da página do pedido. Dado de terceiro é entrada não confiável:
        // um "javascript:..." ou um domínio estranho viraria XSS ou golpe. Só https do próprio PagBank.
        var payLink = created.Links?.FirstOrDefault(l => l.Rel == "PAY")?.Href;
        if (!IsPagBankPaymentLink(payLink))
            throw new PaymentGatewayException($"O PagBank criou o checkout {created.Id} sem um link de pagamento válido.");

        logger.LogInformation("Checkout {CheckoutId} criado no PagBank para o pedido {PedidoId}.", created.Id, charge.OrderId);
        return new PaymentStart(reference, payLink, PixCopyPaste: null, ProviderReference: created.Id);
    }

    public async Task CancelChargeAsync(string? providerReference, CancellationToken ct = default)
    {
        if (providerReference is null || !CheckoutIdFormat().IsMatch(providerReference)) return;

        // Inativado, o checkout não aceita mais pagamento. Já inativo ou inexistente (404) não é erro.
        using var request = new HttpRequestMessage(HttpMethod.Post, $"checkouts/{providerReference}/inactivate");
        await SendAsync<object>(request, "inativar o checkout", ct, notFoundIsNull: true, readBody: false);
        logger.LogInformation("Checkout {CheckoutId} inativado no PagBank.", providerReference);
    }

    // A referência vai na URL de aviso que a própria loja montou (?ref=AML-XXXXXX-P42)
    public string? ClaimedExternalId(PaymentWebhookRequest request) =>
        request.Query.TryGetValue("ref", out var reference) && ReferenceFormat().IsMatch(reference) ? reference : null;

    public async Task<PaymentNotification?> ReadNotificationAsync(PaymentWebhookRequest webhook, CancellationToken ct = default)
    {
        webhook.Headers.TryGetValue("x-payload-signature", out var signature);
        if (!string.IsNullOrWhiteSpace(signature) && !await HasValidSignatureAsync(webhook.RawBody, signature, ct))
        {
            // Diagnóstico sem dado sensível: só quais cabeçalhos de autenticação chegaram e o tamanho de cada
            // assinatura (64 bytes = formato P1363; 70 a 72 = DER). Nunca o valor da assinatura nem o corpo.
            logger.LogWarning("Aviso do PagBank com assinatura inválida; recusado. x-payload-signature: {Assinaturas}; x-authenticity-token: {TemTokenAntigo}; corpo: {Bytes} bytes.",
                DescribeSignatures(signature), webhook.Headers.ContainsKey("x-authenticity-token") ? "presente" : "ausente", webhook.RawBody.Length);
            throw new InvalidPaymentNotificationException("Assinatura do PagBank ausente ou inválida.");
        }

        // Do corpo, só o id do pedido do PagBank. Ele é conferido pelo formato antes de virar parte do
        // endereço consultado na API. A referência vem da URL que a própria loja montou.
        var orderId = ReadOrderId(webhook.Body);
        if (orderId is null || !OrderIdFormat().IsMatch(orderId)) return null;
        if (!webhook.Query.TryGetValue("ref", out var reference) || !ReferenceFormat().IsMatch(reference)) return null;

        return await ReadOrderAsync(orderId, reference, ct);
    }

    public async Task<PaymentNotification?> QueryStatusAsync(string externalId, string? providerReference, CancellationToken ct = default)
    {
        if (providerReference is null || !CheckoutIdFormat().IsMatch(providerReference) || !ReferenceFormat().IsMatch(externalId))
            return null;

        // O checkout lista os pedidos criados a partir dele em orders[] (confirmado no sandbox em 12/11/2026).
        // Cada tentativa de pagamento da cliente vira um pedido; o pago é o que vale.
        using var request = new HttpRequestMessage(HttpMethod.Get, $"checkouts/{providerReference}");
        var checkout = await SendAsync<CheckoutStatusResponse>(request, "consultar o checkout", ct, notFoundIsNull: true);
        PaymentNotification? found = null;
        foreach (var orderId in checkout?.Orders?.Select(o => o.Id) ?? [])
        {
            if (orderId is null || !OrderIdFormat().IsMatch(orderId)) continue;
            var notification = await ReadOrderAsync(orderId, externalId, ct);
            if (notification?.Status is PaymentStatus.Approved or PaymentStatus.Refunded) return notification;
            found ??= notification;
        }
        return found;
    }

    // Consulta o pedido do PagBank e traduz para o nosso aviso. É o mesmo caminho para o webhook e para a
    // conciliação: o que vale é sempre o que a API responde para a conta da loja.
    private async Task<PaymentNotification?> ReadOrderAsync(string orderId, string reference, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"orders/{orderId}");
        var order = await SendAsync<OrderResponse>(request, "consultar o pedido", ct, notFoundIsNull: true);
        if (order?.Charges is not { Count: > 0 } charges) return null;

        // Obrigatório: o pedido do PagBank tem de ser da nossa referência. A loja manda a referência no item do
        // checkout, e o "?ref=" da URL não é protegido por nada; é esta conferência que impede desviar um
        // pagamento real de um pedido para outro.
        var itemReferences = order.Items?.Select(i => i.ReferenceId).Where(r => r is not null).ToList() ?? [];
        if (!itemReferences.Contains(reference))
        {
            logger.LogWarning("ALERTA: pedido {PedidoPagBank} do PagBank não pertence à referência {Referencia} (referências dele: {Referencias}); ignorado.",
                orderId, reference, itemReferences.Count == 0 ? "nenhuma" : string.Join(", ", itemReferences));
            return null;
        }

        // Se uma tentativa foi recusada e outra paga, a paga é a que vale
        var charge = charges.FirstOrDefault(c => c.Status == "PAID") ?? charges[^1];
        if (charge.Status is null) return null;

        // Estorno: o valor devolvido aparece em amount.summary.refunded. A devolução é pedida em /charges/{id}/cancel,
        // e a documentação não diz se depois disso a cobrança fica PAID ou CANCELED (o sandbox não deixou devolver
        // um PIX para conferir). Por isso vale o VALOR, seja qual for o status: devolução total vira Refunded
        // (o pedido é cancelado ou vira alerta). Devolução parcial é decisão da loja (ex.: faltou uma peça) e não
        // muda o pedido. Se vier CANCELED sem valor devolvido para um pagamento aprovado, o OrderService alerta.
        var paidCents = charge.Amount?.Summary?.Paid ?? charge.Amount?.Value;
        var refundedCents = charge.Amount?.Summary?.Refunded ?? 0;
        if (refundedCents > 0)
        {
            if (paidCents is int paid && paid > 0 && refundedCents >= paid)
                return new PaymentNotification($"{orderId}:REFUNDED", reference, PaymentStatus.Refunded, paid / 100m);
            logger.LogInformation("Pedido {PedidoPagBank} do PagBank com devolução parcial de {Centavos} centavos.", orderId, refundedCents);
        }

        var status = MapChargeStatus(charge.Status);
        if (status is null)
        {
            if (charge.Status is not ("WAITING" or "IN_ANALYSIS" or "AUTHORIZED"))
                logger.LogWarning("ALERTA: pedido {PedidoPagBank} do PagBank com status desconhecido {Status} (referência {Referencia}). Confira no painel do PagBank; pode ser contestação (chargeback).",
                    orderId, charge.Status, reference);
            return null;
        }

        // O mesmo aviso pode chegar várias vezes. "pedido do PagBank + status" identifica a mudança: o mesmo
        // par é barrado pelo ProcessedNotification, e um status novo é processado.
        // O valor é o efetivamente pago (summary.paid) quando o PagBank o informa.
        var amount = paidCents is int cents ? cents / 100m : (decimal?)null;
        return new PaymentNotification($"{orderId}:{charge.Status}", reference, status.Value, amount);
    }

    // Traduz o status da cobrança do PagBank para o nosso. null = não muda nada no pedido.
    // Status documentados para a cobrança (charge):
    //   WAITING      aguardando o pagamento (PIX gerado e ainda não pago)
    //   IN_ANALYSIS  cartão em análise de risco pelo PagBank
    //   AUTHORIZED   cartão pré-autorizado, ainda não capturado
    //   PAID         pago
    //   DECLINED     recusado pelo PagBank ou pelo banco do cartão
    //   CANCELED     cancelado (ex.: PIX venceu sem pagamento)
    // Estorno: a documentação não mostra um status próprio. Ele aparece em amount.summary.refunded e é
    // tratado antes deste método. Chargeback: a documentação não diz como aparece; um status desconhecido
    // vira alerta no log (veja ReadNotificationAsync), para a loja olhar no painel do PagBank.
    // Princípio: só muda o nosso pagamento o que é DEFINITIVO no provedor. Status intermediários
    // (WAITING, IN_ANALYSIS, AUTHORIZED) não mudam nada: o nosso Payment já nasce Pending, e um aviso
    // intermediário atrasado não pode reabrir um pagamento que a loja já cancelou (ex.: a cliente
    // gerou um PIX, desistiu e pagou com cartão).
    // Status desconhecido também não muda nada, para um status novo do PagBank não derrubar o webhook.
    public static PaymentStatus? MapChargeStatus(string pagBankStatus) => pagBankStatus switch
    {
        "PAID" => PaymentStatus.Approved,
        "DECLINED" => PaymentStatus.Rejected,
        "CANCELED" => PaymentStatus.Cancelled,
        "WAITING" or "IN_ANALYSIS" or "AUTHORIZED" => null,
        _ => null,
    };

    private async Task<bool> HasValidSignatureAsync(ReadOnlyMemory<byte> body, string signature, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var key = await keyCache.GetAsync(FetchPublicKeyAsync, now, refresh: false, ct);
        if (PagBankSignature.IsValid(body.Span, signature, key)) return true;

        // Pode ser que o PagBank tenha trocado a chave: busca de novo (no máximo a cada 5 minutos) e confere outra vez
        var fresh = await keyCache.GetAsync(FetchPublicKeyAsync, now, refresh: true, ct);
        return !ReferenceEquals(fresh, key) && PagBankSignature.IsValid(body.Span, signature, fresh);
    }

    // Rota da referência da API (GET /public-keys/{type}), confirmada no sandbox em 12/11/2026. O guia de
    // validação mostra "?type=webhook", mas essa rota não existe e o gateway do PagBank responde com erro.
    private static string DescribeSignatures(string? header)
    {
        if (string.IsNullOrWhiteSpace(header)) return "ausente";
        return string.Join(", ", header.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(part =>
        {
            try
            {
                return $"{Convert.FromBase64String(part).Length} bytes";
            }
            catch (FormatException)
            {
                return $"não é Base64 ({part.Length} caracteres)";
            }
        }));
    }

    private async Task<byte[]> FetchPublicKeyAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "public-keys/webhook");
        var response = await SendAsync<PublicKeyResponse>(request, "buscar a chave pública", ct);
        try
        {
            return Convert.FromBase64String(response?.PublicKey ?? throw new PaymentGatewayException("O PagBank não devolveu a chave pública."));
        }
        catch (FormatException ex)
        {
            throw new PaymentGatewayException("A chave pública do PagBank veio num formato inesperado.", ex);
        }
    }

    // Envia e lê a resposta. Toda falha vira PaymentGatewayException. Com notFoundIsNull, 404 vira null
    // (ex.: o "simular notificação" do painel usa um pedido que não existe).
    private async Task<T?> SendAsync<T>(HttpRequestMessage request, string action, CancellationToken ct,
        bool notFoundIsNull = false, bool readBody = true) where T : class
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.Token);
        try
        {
            using var response = await http.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.NotFound && notFoundIsNull)
                return null;
            if (!response.IsSuccessStatusCode)
            {
                // O corpo do erro explica o motivo (ex.: campo inválido). O token nunca vai para o log.
                var error = await response.Content.ReadAsStringAsync(ct);
                logger.LogError("O PagBank recusou {Acao}: {Status} {Erro}", action, (int)response.StatusCode, error);
                throw new PaymentGatewayException($"O PagBank recusou {action} ({(int)response.StatusCode}).");
            }
            return readBody ? await response.Content.ReadFromJsonAsync<T>(ct) : null;
        }
        catch (HttpRequestException ex)
        {
            throw new PaymentGatewayException($"Não foi possível falar com o PagBank para {action}.", ex);
        }
        catch (JsonException ex)
        {
            throw new PaymentGatewayException($"Resposta inesperada do PagBank ao {action}.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new PaymentGatewayException($"O PagBank demorou demais para {action}.", ex);
        }
    }

    private static readonly string[] PagBankDomains = ["pagseguro.uol.com.br", "pagbank.com.br", "pagseguro.com.br"];

    public static bool IsPagBankPaymentLink(string? link) =>
        Uri.TryCreate(link, UriKind.Absolute, out var url)
        && url.Scheme == Uri.UriSchemeHttps
        && PagBankDomains.Any(d => url.Host.Equals(d, StringComparison.OrdinalIgnoreCase)
            || url.Host.EndsWith("." + d, StringComparison.OrdinalIgnoreCase));

    private static string? ReadOrderId(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            return json.RootElement.ValueKind == JsonValueKind.Object
                && json.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
                ? id.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static PaymentMethodConfig InstallmentsConfig(int interestFree)
    {
        // Parcelas só até o limite sem juros: a cliente nunca paga juros, e o valor cobrado continua igual
        // ao do pedido (se a cliente pagasse juros, o total mudaria e o webhook acusaria valor divergente).
        var installments = Math.Max(1, interestFree).ToString(CultureInfo.InvariantCulture);
        return new PaymentMethodConfig("CREDIT_CARD", [
            new ConfigOption("INSTALLMENTS_LIMIT", installments),
            new ConfigOption("INTEREST_FREE_INSTALLMENTS", installments),
        ]);
    }

    private static int ToCents(decimal amount) => (int)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];

    [GeneratedRegex(@"^CHEC_[A-Za-z0-9-]{1,64}$")]
    private static partial Regex CheckoutIdFormat();

    [GeneratedRegex(@"^ORDE_[A-Za-z0-9-]{1,64}$")]
    private static partial Regex OrderIdFormat();

    [GeneratedRegex(@"^[A-Za-z0-9-]{1,64}$")]
    private static partial Regex ReferenceFormat();

    private sealed record CreateCheckout(
        [property: JsonPropertyName("reference_id")] string ReferenceId,
        [property: JsonPropertyName("expiration_date")] string ExpirationDate,
        [property: JsonPropertyName("items")] CheckoutItem[] Items,
        [property: JsonPropertyName("payment_methods")] PaymentMethodType[] PaymentMethods,
        [property: JsonPropertyName("payment_methods_configs"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PaymentMethodConfig[]? PaymentMethodsConfigs,
        [property: JsonPropertyName("soft_descriptor")] string SoftDescriptor,
        [property: JsonPropertyName("redirect_url")] string RedirectUrl,
        [property: JsonPropertyName("return_url")] string ReturnUrl,
        [property: JsonPropertyName("payment_notification_urls")] string[] PaymentNotificationUrls);

    private sealed record CheckoutItem(
        [property: JsonPropertyName("reference_id")] string ReferenceId,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("quantity")] int Quantity,
        [property: JsonPropertyName("unit_amount")] int UnitAmount);

    private sealed record PaymentMethodType([property: JsonPropertyName("type")] string Type);

    private sealed record PaymentMethodConfig(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("config_options")] ConfigOption[] ConfigOptions);

    private sealed record ConfigOption(
        [property: JsonPropertyName("option")] string Option,
        [property: JsonPropertyName("value")] string Value);

    private sealed record Link(
        [property: JsonPropertyName("rel")] string? Rel,
        [property: JsonPropertyName("href")] string? Href);

    private sealed record CheckoutResponse(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("links")] List<Link>? Links);

    private sealed record OrderResponse(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("items")] List<OrderItemResponse>? Items,
        [property: JsonPropertyName("charges")] List<ChargeResponse>? Charges);

    private sealed record OrderItemResponse([property: JsonPropertyName("reference_id")] string? ReferenceId);

    private sealed record ChargeResponse(
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("amount")] ChargeAmount? Amount);

    private sealed record ChargeAmount(
        [property: JsonPropertyName("value")] int? Value,
        [property: JsonPropertyName("summary")] AmountSummary? Summary);

    // Valores em centavos: total cobrado, efetivamente pago e devolvido
    private sealed record AmountSummary(
        [property: JsonPropertyName("total")] int? Total,
        [property: JsonPropertyName("paid")] int? Paid,
        [property: JsonPropertyName("refunded")] int? Refunded);

    private sealed record CheckoutStatusResponse(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("orders")] List<CheckoutOrder>? Orders);

    private sealed record CheckoutOrder([property: JsonPropertyName("id")] string? Id);

    private sealed record PublicKeyResponse([property: JsonPropertyName("public_key")] string? PublicKey);
}
