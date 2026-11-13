namespace AtelieMarylu.Domain.Payments;

// O provedor de pagamento não respondeu como esperado (fora do ar, recusou, demorou demais).
// Mesmo padrão da EmailDeliveryException: quem implementa o IPaymentGateway transforma as falhas
// de HTTP nesta exceção, e as telas tratam só ela, sem saber qual provedor está por trás.
public sealed class PaymentGatewayException(string message, Exception? inner = null) : Exception(message, inner);
