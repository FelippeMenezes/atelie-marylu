using AtelieMarylu.Domain.Shipping;
using AtelieMarylu.Infrastructure.Integrations;
using Microsoft.Extensions.Logging.Abstractions;

namespace AtelieMarylu.Tests;

public class ShippingQuoteAggregatorTests
{
    private static readonly ShippingPackage[] Packages = [new(300, 5, 20, 25, 1, 50m)];

    private static readonly ShippingOption Pac = new("me-1", "Correios PAC", 25m, 9);

    [Fact]
    public async Task Joins_all_sources_keeping_the_pickup_first()
    {
        var aggregator = Aggregator(new StudioPickupCalculator(), new FixedSource(Pac));

        var options = await aggregator.QuoteAsync("01310100", Packages);

        Assert.Equal([StudioPickupCalculator.Code, "me-1"], options.Select(o => o.Code));
    }

    [Fact]
    public async Task Sources_are_quoted_at_the_same_time()
    {
        // As duas fontes só terminam quando ambas já começaram: em sequência, o teste travaria
        var bothStarted = new TaskCompletionSource();
        var started = 0;
        async Task<IReadOnlyList<ShippingOption>> Wait(ShippingOption option)
        {
            if (Interlocked.Increment(ref started) == 2) bothStarted.SetResult();
            await bothStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            return [option];
        }
        var aggregator = Aggregator(new DelegateSource(() => Wait(StudioPickupCalculator.Option)), new DelegateSource(() => Wait(Pac)));

        var options = await aggregator.QuoteAsync("01310100", Packages);

        Assert.Equal(2, options.Count);
    }

    [Fact]
    public async Task Invalid_postal_code_throws()
    {
        var aggregator = Aggregator(new StudioPickupCalculator());

        await Assert.ThrowsAsync<ArgumentException>(() => aggregator.QuoteAsync("123", Packages));
    }

    [Fact]
    public async Task When_the_only_carrier_fails_it_does_not_pretend_the_store_only_offers_pickup()
    {
        // "Só retirada" diria a uma cliente de outro estado que a loja não entrega para ela
        var aggregator = Aggregator(new StudioPickupCalculator(), new FailingSource());

        await Assert.ThrowsAsync<ShippingQuoteException>(() => aggregator.QuoteAsync("01310100", Packages));
    }

    [Fact]
    public async Task When_every_source_fails_it_throws()
    {
        var aggregator = Aggregator(new FailingSource(), new FailingSource());

        await Assert.ThrowsAsync<ShippingQuoteException>(() => aggregator.QuoteAsync("01310100", Packages));
    }

    [Fact]
    public async Task When_one_carrier_fails_the_others_are_still_offered()
    {
        // Regra genérica do agregador: com mais de um provedor de entrega, a queda de um não esconde o outro
        var other = new ShippingOption("other-1", "Outro provedor", 18m, 1);
        var aggregator = Aggregator(new StudioPickupCalculator(), new FailingSource(), new FixedSource(other));

        var options = await aggregator.QuoteAsync("01310100", Packages);

        Assert.Equal([StudioPickupCalculator.Code, "other-1"], options.Select(o => o.Code));
    }

    [Fact]
    public async Task No_carrier_serving_the_postal_code_offers_only_pickup()
    {
        // A fonte respondeu, só não atende o CEP: aqui "só retirada" é a verdade, não uma falha
        var aggregator = Aggregator(new StudioPickupCalculator(), new FixedSource());

        var options = await aggregator.QuoteAsync("01310100", Packages);

        Assert.Equal([StudioPickupCalculator.Code], options.Select(o => o.Code));
    }

    [Fact]
    public async Task Invalid_or_repeated_options_are_dropped()
    {
        // Com código repetido, o pedido poderia gravar o preço de uma opção e o nome de outra
        var aggregator = Aggregator(
            new StudioPickupCalculator(),
            new FixedSource(Pac, new ShippingOption("me-1", "Outro com o mesmo código", 1m, 1)),
            new FixedSource(new ShippingOption("", "Sem código", 10m, 1), new ShippingOption("me-9", "Negativo", -5m, 1)));

        var options = await aggregator.QuoteAsync("01310100", Packages);

        Assert.Equal([StudioPickupCalculator.Code, "me-1"], options.Select(o => o.Code));
        Assert.Equal(25m, options.Single(o => o.Code == "me-1").Amount);
    }

    private static ShippingQuoteAggregator Aggregator(params IShippingCalculator[] sources) =>
        new(sources, NullLogger<ShippingQuoteAggregator>.Instance);

    private sealed class FixedSource(params ShippingOption[] options) : IShippingCalculator
    {
        public Task<IReadOnlyList<ShippingOption>> QuoteAsync(string destinationPostalCode, IReadOnlyList<ShippingPackage> packages, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ShippingOption>>(options);
    }

    private sealed class DelegateSource(Func<Task<IReadOnlyList<ShippingOption>>> quote) : IShippingCalculator
    {
        public Task<IReadOnlyList<ShippingOption>> QuoteAsync(string destinationPostalCode, IReadOnlyList<ShippingPackage> packages, CancellationToken ct = default) =>
            quote();
    }

    internal sealed class FailingSource : IShippingCalculator
    {
        public Task<IReadOnlyList<ShippingOption>> QuoteAsync(string destinationPostalCode, IReadOnlyList<ShippingPackage> packages, CancellationToken ct = default) =>
            throw new ShippingQuoteException("Melhor Envio fora do ar");
    }
}
