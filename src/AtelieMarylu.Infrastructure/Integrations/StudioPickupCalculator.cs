using AtelieMarylu.Domain.Shipping;

namespace AtelieMarylu.Infrastructure.Integrations;

// A cliente busca no ateliê: grátis, sem prazo de transporte e sem depender de nenhum serviço de fora
// (por isso nunca falha). É uma das fontes do ShippingQuoteAggregator.
public sealed class StudioPickupCalculator : IShippingCalculator
{
    // Gravado nos pedidos (Order.ShippingCode): não mudar, ou os pedidos antigos perdem a referência
    public const string Code = "retirada";

    public static readonly ShippingOption Option = new(Code, "Pickup at the studio", 0m, null);

    public Task<IReadOnlyList<ShippingOption>> QuoteAsync(string destinationPostalCode, IReadOnlyList<ShippingPackage> packages, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ShippingOption>>([Option]);
}
