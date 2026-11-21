using AtelieMarylu.Domain.Shipping;

namespace AtelieMarylu.Infrastructure.Integrations;

// Tabela fixa, só para desenvolvimento e testes (sem token do Melhor Envio). Fora de Development nunca é usada:
// sem token, a produção oferece só a retirada no ateliê, para não cobrar um frete inventado.
public class SimulatedShippingCalculator : IShippingCalculator
{
    public const string PickupCode = StudioPickupCalculator.Code;

    public Task<IReadOnlyList<ShippingOption>> QuoteAsync(string destinationPostalCode, IReadOnlyList<ShippingPackage> packages, CancellationToken ct = default)
    {
        if (PostalCode.Normalize(destinationPostalCode) is null)
            throw new ArgumentException("Invalid postal code.", nameof(destinationPostalCode));

        var kilos = Math.Ceiling(packages.Sum(p => p.WeightGrams * p.Quantity) / 1000m);
        if (kilos < 1) kilos = 1;

        IReadOnlyList<ShippingOption> options =
        [
            StudioPickupCalculator.Option,
            new("pac", "PAC (simulated)", 20m + 5m * (kilos - 1), 8),
            new("sedex", "SEDEX (simulated)", 35m + 8m * (kilos - 1), 3),
        ];
        return Task.FromResult(options);
    }
}
