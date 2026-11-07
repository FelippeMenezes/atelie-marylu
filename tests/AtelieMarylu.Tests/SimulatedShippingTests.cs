using AtelieMarylu.Infrastructure.Integrations;
using AtelieMarylu.Domain.Shipping;

namespace AtelieMarylu.Tests;

public class SimulatedShippingTests
{
    private readonly SimulatedShippingCalculator shipping = new();

    [Fact]
    public async Task Includes_free_pickup()
    {
        var options = await shipping.QuoteAsync("20000-000", [new ShippingPackage(300, 5, 20, 25, 1)]);

        Assert.Contains(options, o => o.Code == SimulatedShippingCalculator.PickupCode && o.Amount == 0);
    }

    [Fact]
    public async Task Shipping_increases_with_weight()
    {
        var leve = await shipping.QuoteAsync("20000000", [new ShippingPackage(300, 5, 20, 25, 1)]);
        var heavy = await shipping.QuoteAsync("20000000", [new ShippingPackage(1500, 5, 20, 25, 2)]);

        Assert.True(heavy.First(o => o.Code == "pac").Amount > leve.First(o => o.Code == "pac").Amount);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("abcdefgh")]
    [InlineData("")]
    public async Task Invalid_postal_code_throws(string postalCode)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => shipping.QuoteAsync(postalCode, [new ShippingPackage(300, 5, 20, 25, 1)]));
    }

    [Theory]
    [InlineData("20000-000", "20000000")]
    [InlineData(" 20000000 ", "20000000")]
    [InlineData("2000-000", null)]
    public void Normalizes_postal_code(string input, string? expected)
    {
        Assert.Equal(expected, PostalCode.Normalize(input));
    }
}
