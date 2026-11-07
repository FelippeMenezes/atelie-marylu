namespace AtelieMarylu.Domain.Shipping;

// Contrato que qualquer provedor de frete precisa cumprir.
// Hoje existe só a implementação simulada; na fase de frete entra a do Melhor Envio.
public interface IShippingCalculator
{
    Task<IReadOnlyList<ShippingOption>> QuoteAsync(string destinationPostalCode, IReadOnlyList<ShippingPackage> packages, CancellationToken ct = default);
}
