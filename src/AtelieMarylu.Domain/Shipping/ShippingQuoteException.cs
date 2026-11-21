namespace AtelieMarylu.Domain.Shipping;

// O provedor de frete não respondeu como esperado (fora do ar, recusou, demorou demais).
// Mesmo padrão da PaymentGatewayException: quem implementa o IShippingCalculator transforma as falhas
// de HTTP nesta exceção, e o resto da loja trata só ela, sem saber qual provedor está por trás.
public sealed class ShippingQuoteException(string message, Exception? inner = null) : Exception(message, inner);
