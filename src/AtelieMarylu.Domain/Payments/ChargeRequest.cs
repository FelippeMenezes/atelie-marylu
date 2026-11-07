namespace AtelieMarylu.Domain.Payments;

public record ChargeRequest(int OrderId, string OrderCode, decimal Amount, PaymentMethod Method, string CustomerEmail, string CustomerName, string Description, DateTime ExpiresAt);
