using AtelieMarylu.Domain.Shipping;
using AtelieMarylu.Infrastructure.Integrations;

namespace AtelieMarylu.Tests;

// O painel usa esta regra para dizer à Marylu se ela espera a cliente, espera a coleta ou leva o pacote
public class ShippingHandoverTests
{
    [Theory]
    [InlineData("retirada", ShippingHandover.CustomerPickup)]
    [InlineData("me-32", ShippingHandover.CarrierPickup)]   // Loggi Coleta
    [InlineData("me-2", ShippingHandover.DropOff)]          // Correios SEDEX
    [InlineData("me-34", ShippingHandover.DropOff)]         // Loggi Ponto
    [InlineData("pac", ShippingHandover.DropOff)]           // simulado / pedidos antigos
    [InlineData("codigo-desconhecido", ShippingHandover.DropOff)] // na dúvida, o caso que exige ação
    public void Decides_how_the_package_leaves(string code, ShippingHandover expected)
    {
        Assert.Equal(expected, ShippingHandoverRules.For(code));
    }

    [Fact]
    public void Codes_match_the_ones_written_by_the_calculators()
    {
        // O Domain repete os códigos porque não pode depender da Infrastructure: se um lado mudar, o painel erra
        Assert.Equal(StudioPickupCalculator.Code, ShippingHandoverRules.StudioPickupCode);
        Assert.StartsWith(MelhorEnvioShippingCalculator.CodePrefix, "me-32");
    }
}
