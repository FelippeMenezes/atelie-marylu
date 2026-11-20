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

    // O provedor devolveu a cobrança (link, código PIX ou referência). Sem isso, a cliente não tem como pagar.
    public bool HasStarted => ExternalId is not null;

    // Quanto tempo uma tentativa pode ficar "abrindo" (pendente, ainda sem resposta do provedor) antes de contar
    // como "não abriu". Bem acima do limite das chamadas ao provedor (10 s): antes disso, a chamada pode estar em
    // andamento, e oferecer "Tentar de novo" cancelaria uma cobrança que ainda vai abrir.
    public static readonly TimeSpan OpeningWindow = TimeSpan.FromMinutes(1);

    // Ainda esperando o provedor devolver a cobrança
    public bool IsOpening(DateTime utcNow) =>
        Status == PaymentStatus.Pending && !HasStarted && utcNow - CreatedAt <= OpeningWindow;

    // Não abriu: o provedor falhou (Failed) ou a tentativa ficou pendente sem resposta além do prazo
    // (ex.: a conexão caiu no meio da chamada, ou tentativas de antes do status Failed existir)
    public bool DidNotOpen(DateTime utcNow) =>
        Status == PaymentStatus.Failed
        || (Status == PaymentStatus.Pending && !HasStarted && utcNow - CreatedAt > OpeningWindow);

    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; private set; }

    public static string Describe(PaymentStatus status) => status switch
    {
        PaymentStatus.Pending => "Pending",
        PaymentStatus.Approved => "Approved",
        PaymentStatus.Rejected => "Rejected",
        PaymentStatus.Cancelled => "Cancelled",
        PaymentStatus.Refunded => "Refunded",
        PaymentStatus.Failed => "Did not open",
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

    // Chamado quando o provedor falha ao abrir a cobrança (PaymentGatewayException em StartAsync).
    // Tira a tentativa de "Pendente" para a tela da cliente mostrar "o pagamento não abriu" e "Tentar de novo".
    public void MarkStartFailed()
    {
        // Já saiu de Pendente (outra ação cancelou, ou foi aprovado): nada a fazer.
        // Idempotente, e nunca sobrescreve um status final como Approved.
        if (Status != PaymentStatus.Pending) return;

        // Pendente COM cobrança aberta: marcar como falha esconderia um link que a cliente pode pagar.
        // Só acontece por erro de programação, então falha na hora em vez de gravar um dado errado.
        if (HasStarted)
            throw new InvalidOperationException($"O pagamento {Id} já abriu no provedor e não pode ser marcado como falha.");

        Status = PaymentStatus.Failed;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateStatus(PaymentStatus isNew)
    {
        Status = isNew;
        UpdatedAt = DateTime.UtcNow;
    }
}
