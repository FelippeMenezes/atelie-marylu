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

    // Quem processou: "simulated", "manual" e, no futuro, "pagbank"
    public string Provider { get; private set; } = "";

    // Id do pagamento no provedor. É por ele que o webhook encontra este registro.
    public string? ExternalId { get; private set; }

    // Id da cobrança aberta no provedor (ex.: checkout do PagBank). Serve para inativá-la quando
    // este pagamento é cancelado: cancelar só no nosso banco deixaria a cliente pagar mesmo assim.
    public string? ProviderReference { get; private set; }

    // Para onde mandar o cliente (checkout do provedor) ou o código PIX copia e cola
    public string? PaymentUrl { get; private set; }
    public string? PixCopyPaste { get; private set; }

    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; private set; }

    public static string Describe(PaymentStatus status) => status switch
    {
        PaymentStatus.Pending => "Pending",
        PaymentStatus.Approved => "Approved",
        PaymentStatus.Rejected => "Rejected",
        PaymentStatus.Cancelled => "Cancelled",
        PaymentStatus.Refunded => "Refunded",
        _ => status.ToString(),
    };

    public void RecordStart(string externalId, string? paymentUrl, string? pixCopyPaste, string? providerReference = null)
    {
        ExternalId = externalId;
        ProviderReference = providerReference;
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
