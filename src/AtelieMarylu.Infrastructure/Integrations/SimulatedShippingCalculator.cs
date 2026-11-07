using AtelieMarylu.Domain.Shipping;

namespace AtelieMarylu.Infrastructure.Integrations;

// Tabela fixa só para o MVP. Será trocada pela cotação real do Melhor Envio.
public class SimulatedShippingCalculator : IShippingCalculator
{
    public const string PickupCode = "retirada";

    public Task<IReadOnlyList<ShippingOption>> QuoteAsync(string destinationPostalCode, IReadOnlyList<ShippingPackage> packages, CancellationToken ct = default)
    {
        if (PostalCode.Normalize(destinationPostalCode) is null)
            throw new ArgumentException("CEP inválido.", nameof(destinationPostalCode));

        var kilos = Math.Ceiling(packages.Sum(p => p.WeightGrams * p.Quantity) / 1000m);
        if (kilos < 1) kilos = 1;

        IReadOnlyList<ShippingOption> options =
        [
            new(PickupCode, "Retirada no ateliê", 0m, null),
            new("pac", "PAC (simulado)", 20m + 5m * (kilos - 1), 8),
            new("sedex", "SEDEX (simulado)", 35m + 8m * (kilos - 1), 3),
        ];
        return Task.FromResult(options);
    }
}
