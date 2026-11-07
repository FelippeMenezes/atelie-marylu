namespace AtelieMarylu.Domain.Payments;

public enum PaymentStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Cancelled = 4,
    Refunded = 5,
}
