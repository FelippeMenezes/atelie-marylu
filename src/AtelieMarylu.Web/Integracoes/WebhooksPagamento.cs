using AtelieMarylu.Domain.Pagamentos;
using AtelieMarylu.Web.Services;

namespace AtelieMarylu.Web.Integracoes;

// Endpoint que o provedor de pagamento chama para avisar que o status mudou.
// É ele (e não a tela de "obrigado") que confirma o pedido como pago.
public static class WebhooksPagamento
{
    public static void MapWebhooksPagamento(this WebApplication app)
    {
        // O gateway simulado não autentica a chamada, então só fica exposto em desenvolvimento.
        // Com o Mercado Pago, o próprio gateway valida a assinatura e o endpoint vale para produção.
        if (!app.Environment.IsDevelopment())
            return;

        app.MapPost("/api/webhooks/pagamento", async (HttpRequest request, IGatewayPagamento gateway, PedidoService pedidos, CancellationToken ct) =>
        {
            using var leitor = new StreamReader(request.Body);
            var corpo = await leitor.ReadToEndAsync(ct);
            var cabecalhos = request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);

            var notificacao = await gateway.LerNotificacaoAsync(corpo, cabecalhos, ct);
            if (notificacao is null)
                return Results.BadRequest();

            var encontrado = await pedidos.ProcessarNotificacaoAsync(notificacao, ct);
            return encontrado ? Results.Ok() : Results.NotFound();
        }).DisableAntiforgery();
    }
}
