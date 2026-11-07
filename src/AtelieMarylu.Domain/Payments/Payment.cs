namespace AtelieMarylu.Domain.Payments;

// Uma tentativa de pagamento de um pedido. Um pedido pode ter várias
// (ex.: cartão recusado e depois pago com PIX).
public class Payment
{
    private Payment() { } // EF Core

    public Payment(PaymentMethod method, decimal amount, string provider)
    {
        Method = method;
        Amount = amount;
        Provider = provider;
    }

    public int Id { get; private set; }
    public int OrderId { get; private set; }
    public PaymentMethod Method { get; private set; }
    public decimal Amount { get; private set; }
    public PaymentStatus Status { get; private set; } = PaymentStatus.Pending;

    // Quem processou: "simulado", "manual" e, no futuro, "mercadopago"
    public string Provider { get; private set; } = "";

    // Id do pagamento no provedor. É por ele que o webhook encontra este registro.
    public string? ExternalId { get; private set; }

    // Para onde mandar o cliente (checkout do provedor) ou o código PIX copia e cola
    public string? PaymentUrl { get; private set; }
    public string? PixCopyPaste { get; private set; }

    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; private set; }

    public static string Describe(PaymentStatus status) => status switch
    {
        PaymentStatus.Pending => "Pendente",
        PaymentStatus.Approved => "Aprovado",
        PaymentStatus.Rejected => "Recusado",
        PaymentStatus.Cancelled => "Cancelado",
        PaymentStatus.Refunded => "Estornado",
        _ => status.ToString(),
    };

    public void RecordStart(string externalId, string? paymentUrl, string? pixCopyPaste)
    {
        ExternalId = externalId;
        PaymentUrl = paymentUrl;
        PixCopyPaste = pixCopyPaste;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateStatus(PaymentStatus isNew)
    {
        Status = isNew;
        UpdatedAt = DateTime.UtcNow;
    }
}
