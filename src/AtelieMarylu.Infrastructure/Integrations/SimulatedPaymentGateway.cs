using System.Text.Json;
using AtelieMarylu.Domain.Payments;

namespace AtelieMarylu.Infrastructure.Integrations;

// Provedor de pagamento de mentira, para o fluxo inteiro funcionar antes do PagBank.
// O pagamento fica "Pendente" até alguém aprovar pela tela do pedido (botão só em desenvolvimento)
// ou mandar um POST para /api/webhooks/payment, imitando o que o provedor real faz.
public class SimulatedPaymentGateway : IPaymentGateway
{
    public const string ProviderName = "simulated";

    public string Name => ProviderName;

    // Qualquer um pode mandar um POST imitando o provedor: por isso o webhook dele só existe em Development
    public bool VerifiesNotifications => false;

    public Task<PaymentStart> StartAsync(ChargeRequest charge, CancellationToken ct = default)
    {
        var externalId = $"sim-{Guid.NewGuid():N}";
        var pixPrice = charge.Method == PaymentMethod.Pix
            ? $"00020126PIXSIMULADO-{charge.OrderCode}-{charge.Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}"
            : null;
        return Task.FromResult(new PaymentStart(externalId, PaymentUrl: null, PixCopyPaste: pixPrice));
    }

    // O simulado não abre cobrança em lugar nenhum: não há o que fechar
    public Task CancelChargeAsync(string? providerReference, CancellationToken ct = default) => Task.CompletedTask;

    // O simulado só muda de status pelo webhook ou pelo botão de desenvolvimento
    public Task<PaymentNotification?> QueryStatusAsync(string externalId, string? providerReference, CancellationToken ct = default) =>
        Task.FromResult<PaymentNotification?>(null);

    public string? ClaimedExternalId(PaymentWebhookRequest request)
    {
        try
        {
            return JsonSerializer.Deserialize<SimulatedBody>(request.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web))?.ExternalId;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public Task<PaymentNotification?> ReadNotificationAsync(PaymentWebhookRequest request, CancellationToken ct = default)
    {
        // Formato esperado: { "eventId": "opcional", "externalId": "sim-...", "status": "Approved", "amountPaid": 99.90 }
        // O provedor real exige validar a autenticidade do aviso e consultar o pagamento na API dele.
        try
        {
            var data = JsonSerializer.Deserialize<SimulatedBody>(request.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (data?.ExternalId is null || !Enum.TryParse<PaymentStatus>(data.Status, true, out var status))
                return Task.FromResult<PaymentNotification?>(null);
            var eventId = string.IsNullOrWhiteSpace(data.EventId) ? $"{data.ExternalId}:{status}" : data.EventId;
            return Task.FromResult<PaymentNotification?>(new PaymentNotification(eventId, data.ExternalId, status, data.AmountPaid));
        }
        catch (JsonException)
        {
            return Task.FromResult<PaymentNotification?>(null);
        }
    }

    private sealed record SimulatedBody(string? EventId, string? ExternalId, string? Status, decimal? AmountPaid);
}
