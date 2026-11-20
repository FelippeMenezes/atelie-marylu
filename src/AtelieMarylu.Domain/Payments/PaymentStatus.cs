namespace AtelieMarylu.Domain.Payments;

public enum PaymentStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Cancelled = 4,
    Refunded = 5,

    // A cobrança nem chegou a abrir no provedor (ele recusou, caiu ou não respondeu a tempo).
    // Nada foi cobrado e a cliente nunca recebeu link nem código: a tela oferece "Tentar de novo".
    // Fica gravado como texto ("Failed"), então o valor novo não precisa de migration.
    Failed = 6,
}
