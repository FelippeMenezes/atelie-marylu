using System.Text.Json;
using AtelieMarylu.Domain.Payments;

namespace AtelieMarylu.Infrastructure.Integrations;

// Provedor de pagamento de mentira, para o fluxo inteiro funcionar antes do Mercado Pago.
// O pagamento fica "Pendente" até alguém aprovar pela tela do pedido (botão só em desenvolvimento)
// ou mandar um POST para /api/webhooks/payment, imitando o que o provedor real faz.
public class SimulatedPaymentGateway : IPaymentGateway
{
    public const string ProviderName = "simulated";

    public string Name => ProviderName;

    public Task<PaymentStart> StartAsync(ChargeRequest charge, CancellationToken ct = default)
    {
        var externalId = $"sim-{Guid.NewGuid():N}";
        var pixPrice = charge.Method == PaymentMethod.Pix
            ? $"00020126PIXSIMULADO-{charge.OrderCode}-{charge.Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}"
            : null;
        return Task.FromResult(new PaymentStart(externalId, PaymentUrl: null, PixCopyPaste: pixPrice));
    }

    public Task<PaymentNotification?> ReadNotificationAsync(string body, IReadOnlyDictionary<string, string> headers, CancellationToken ct = default)
    {
        // Formato esperado: { "eventId": "opcional", "externalId": "sim-...", "status": "Approved", "amountPaid": 99.90 }
        // O Mercado Pago real exige validar a assinatura (x-signature) e consultar o pagamento na API deles.
        try
        {
            var data = JsonSerializer.Deserialize<SimulatedBody>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
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
