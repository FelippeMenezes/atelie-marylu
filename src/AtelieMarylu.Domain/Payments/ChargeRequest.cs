namespace AtelieMarylu.Domain.Payments;

// PaymentId é o Id do nosso registro Payment: serve de referência única da cobrança no provedor.
// InterestFreeInstallments: até quantas parcelas sem juros o cartão aceita (a loja assume os juros).
public record ChargeRequest(int OrderId, int PaymentId, string OrderCode, decimal Amount, PaymentMethod Method, string CustomerEmail, string CustomerName, string Description, DateTime ExpiresAt, int InterestFreeInstallments = 1);
