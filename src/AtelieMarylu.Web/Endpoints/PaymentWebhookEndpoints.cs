using AtelieMarylu.Domain.Payments;
using AtelieMarylu.Web.Services;

namespace AtelieMarylu.Web.Endpoints;

// Endpoint que o provedor de pagamento chama para avisar que o status mudou.
// É ele (e não a tela de "obrigado") que confirma o pedido como pago.
public static class PaymentWebhookEndpoints
{
    public static void MapPaymentWebhooks(this WebApplication app)
    {
        // O gateway simulado não autentica a chamada, então só fica exposto em desenvolvimento.
        // Com o Mercado Pago, o próprio gateway valida a assinatura e o endpoint vale para produção.
        if (!app.Environment.IsDevelopment())
            return;

        app.MapPost("/api/webhooks/pagamento", async (HttpRequest request, IPaymentGateway gateway, OrderService orders, CancellationToken ct) =>
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync(ct);
            var headers = request.Headers.ToDictionary(header => header.Key, header => header.Value.ToString(), StringComparer.OrdinalIgnoreCase);

            var notification = await gateway.ReadNotificationAsync(body, headers, ct);
            if (notification is null)
                return Results.BadRequest();

            var found = await orders.ProcessNotificationAsync(notification, ct);
            return found ? Results.Ok() : Results.NotFound();
        }).DisableAntiforgery();
    }
}
