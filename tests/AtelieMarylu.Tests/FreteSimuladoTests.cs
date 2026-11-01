using AtelieMarylu.Domain.Frete;

namespace AtelieMarylu.Tests;

public class FreteSimuladoTests
{
    private readonly FreteSimulado frete = new();

    [Fact]
    public async Task Inclui_retirada_gratis()
    {
        var opcoes = await frete.CotarAsync("20000-000", [new PacoteFrete(300, 5, 20, 25, 1)]);

        Assert.Contains(opcoes, o => o.Codigo == FreteSimulado.CodigoRetirada && o.Valor == 0);
    }

    [Fact]
    public async Task Frete_aumenta_com_o_peso()
    {
        var leve = await frete.CotarAsync("20000000", [new PacoteFrete(300, 5, 20, 25, 1)]);
        var pesado = await frete.CotarAsync("20000000", [new PacoteFrete(1500, 5, 20, 25, 2)]);

        Assert.True(pesado.First(o => o.Codigo == "pac").Valor > leve.First(o => o.Codigo == "pac").Valor);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("abcdefgh")]
    [InlineData("")]
    public async Task Cep_invalido_gera_erro(string cep)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => frete.CotarAsync(cep, [new PacoteFrete(300, 5, 20, 25, 1)]));
    }

    [Theory]
    [InlineData("20000-000", "20000000")]
    [InlineData(" 20000000 ", "20000000")]
    [InlineData("2000-000", null)]
    public void Normaliza_cep(string entrada, string? esperado)
    {
        Assert.Equal(esperado, Cep.Normalizar(entrada));
    }
}
