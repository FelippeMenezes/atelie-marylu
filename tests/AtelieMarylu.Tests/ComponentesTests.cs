using System.Globalization;
using AtelieMarylu.Domain.Pedidos;
using AtelieMarylu.Web.Components.Shared;
using AtelieMarylu.Web.Configuracao;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AtelieMarylu.Tests;

// Testes de componentes Blazor com bUnit: renderizam o componente em memória, sem navegador
public class ComponentesTests : BunitContext
{
    [Fact]
    public void Quantidade_respeita_o_maximo()
    {
        var valor = 2;
        var cut = Render<Quantidade>(p => p
            .Add(x => x.Valor, 2)
            .Add(x => x.Maximo, 3)
            .Add(x => x.ValorChanged, (int v) => valor = v));

        cut.Find("button[aria-label=Aumentar]").Click();
        Assert.Equal(3, valor);

        cut.Render(p => p.Add(x => x.Valor, 3));
        Assert.True(cut.Find("button[aria-label=Aumentar]").HasAttribute("disabled"));
    }

    [Fact]
    public void Quantidade_nao_desce_abaixo_do_minimo()
    {
        var cut = Render<Quantidade>(p => p.Add(x => x.Valor, 1));

        Assert.True(cut.Find("button[aria-label=Diminuir]").HasAttribute("disabled"));
    }

    [Theory]
    [InlineData(StatusPedido.AguardandoPagamento, "Aguardando pagamento")]
    [InlineData(StatusPedido.Cancelado, "Cancelado")]
    public void Status_do_pedido_em_portugues(StatusPedido status, string texto)
    {
        var cut = Render<StatusPedidoBadge>(p => p.Add(x => x.Status, status));

        Assert.Equal(texto, cut.Find("span").TextContent);
        Assert.Contains($"status-{status}", cut.Find("span").ClassName);
    }

    [Fact]
    public void Preco_mostra_desconto_pix_e_parcelas()
    {
        CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
        Services.AddSingleton(Options.Create(new LojaOptions { DescontoPixPercentual = 10, ParcelasSemJuros = 3 }));

        var cut = Render<PrecoProduto>(p => p.Add(x => x.Preco, 90m));
        // O formato brasileiro usa espaço não separável entre "R$" e o valor
        var texto = cut.Markup.Replace('\u00A0', ' ');

        Assert.Contains("R$ 90,00", texto);
        Assert.Contains("R$ 81,00", texto);       // 10% off no PIX
        Assert.Contains("3x de R$ 30,00", texto);
    }

    [Fact]
    public void Foto_sem_imagem_mostra_o_logo()
    {
        var cut = Render<FotoProduto>(p => p.Add(x => x.Nome, "Bolsa"));

        Assert.Contains("img/logo.png", cut.Markup);
    }
}
