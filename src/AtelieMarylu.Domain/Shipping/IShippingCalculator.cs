namespace AtelieMarylu.Domain.Shipping;

// Contrato que qualquer provedor de frete precisa cumprir. Na loja, quem responde é o ShippingQuoteAggregator
// (Infrastructure), que junta a retirada no ateliê com os provedores (o Melhor Envio, para entregas locais e distantes).
// Falha do provedor vira ShippingQuoteException; CEP inválido, ArgumentException.
public interface IShippingCalculator
{
    Task<IReadOnlyList<ShippingOption>> QuoteAsync(string destinationPostalCode, IReadOnlyList<ShippingPackage> packages, CancellationToken ct = default);
}
