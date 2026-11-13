using AtelieMarylu.Domain.Payments;
using AtelieMarylu.Web.Services;

namespace AtelieMarylu.Web.Endpoints;

// Endpoint que o provedor de pagamento chama para avisar que o status mudou.
// É ele (e não a tela de "obrigado") que confirma o pedido como pago.
public static class PaymentWebhookEndpoints
{
    // Uma notificação de pagamento tem poucos KB. Mais do que isso não é do provedor.
    private const long MaxBodyBytes = 16 * 1024;

    public static void MapPaymentWebhooks(this WebApplication app)
    {
        var isDevelopment = app.Environment.IsDevelopment();

        app.MapPost("/api/webhooks/payment", async (HttpRequest request, IPaymentGateway gateway, OrderService orders, CancellationToken ct) =>
        {
            // O site reexecuta respostas de erro sem corpo como a página "não encontrada" (UseStatusCodePagesWithReExecute).
            // Num POST, essa página recusa o pedido (antiforgery) e o provedor receberia 400 em vez de 401, 404 ou 503.
            // Para uma API, o código de status é a resposta: desliga a reexecução aqui.
            if (request.HttpContext.Features.Get<Microsoft.AspNetCore.Diagnostics.IStatusCodePagesFeature>() is { } statusPages)
                statusPages.Enabled = false;

            // O gateway simulado aceita qualquer aviso, então fora de Development o endpoint só responde
            // quando um provedor real, que confere cada aviso na API dele, está ligado.
            if (!isDevelopment && !gateway.VerifiesNotifications)
                return Results.NotFound();

            if (request.ContentLength > MaxBodyBytes)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

            var body = await ReadLimitedAsync(request.Body, ct);
            if (body is null)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

            var webhook = new PaymentWebhookRequest(
                body,
                request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase),
                request.Query.ToDictionary(q => q.Key, q => q.Value.ToString(), StringComparer.OrdinalIgnoreCase));

            // Filtro barato antes de qualquer chamada ao provedor: o aviso precisa apontar para um pagamento que a
            // loja criou. Avisos com referência desconhecida param aqui, sem gastar a credencial da loja.
            if (gateway.ClaimedExternalId(webhook) is not { } claimed || !await orders.HasPaymentAsync(gateway.Name, claimed, ct))
                return Results.NotFound();

            PaymentNotification? notification;
            try
            {
                notification = await gateway.ReadNotificationAsync(webhook, ct);
            }
            catch (InvalidPaymentNotificationException)
            {
                // Não veio do provedor (ou foi alterado no caminho). 401, e não 400, para aparecer separado no log.
                return Results.Unauthorized();
            }
            catch (PaymentGatewayException)
            {
                // Não conseguimos consultar o provedor agora. Responder erro faz ele mandar de novo mais tarde.
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
            // Aviso autêntico, mas que não muda nada (ex.: status intermediário): responder 200 evita que o
            // provedor fique reenviando o mesmo aviso.
            if (notification is null)
                return Results.Ok();

            var found = await orders.ProcessNotificationAsync(notification, ct);
            return found ? Results.Ok() : Results.NotFound();
        }).DisableAntiforgery();
    }

    // Lê o corpo cru até o limite. O Content-Length pode faltar (envio em partes), então conta de verdade.
    private static async Task<byte[]?> ReadLimitedAsync(Stream body, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        int read;
        while ((read = await body.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes) return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }
}
