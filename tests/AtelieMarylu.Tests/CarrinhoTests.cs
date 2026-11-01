using AtelieMarylu.Domain.Carrinho;

namespace AtelieMarylu.Tests;

public class CarrinhoTests
{
    [Fact]
    public void Adicionar_o_mesmo_produto_soma_quantidade()
    {
        var carrinho = new Carrinho();

        carrinho.Adicionar(1, 2);
        carrinho.Adicionar(1, 3);

        Assert.Single(carrinho.Itens);
        Assert.Equal(5, carrinho.TotalDeUnidades);
    }

    [Fact]
    public void Quantidade_zero_remove_o_item()
    {
        var carrinho = new Carrinho();
        carrinho.Adicionar(1);

        carrinho.AlterarQuantidade(1, 0);

        Assert.True(carrinho.Vazio);
    }

    [Fact]
    public void Quantidade_tem_limite_por_item()
    {
        var carrinho = new Carrinho();

        carrinho.Adicionar(1, 999);

        Assert.Equal(Carrinho.QuantidadeMaximaPorItem, carrinho.Itens[0].Quantidade);
    }

    [Fact]
    public void Ignora_quantidades_invalidas_vindas_do_navegador()
    {
        var carrinho = new Carrinho([new ItemCarrinho(1, -5), new ItemCarrinho(2, 1)]);

        Assert.Single(carrinho.Itens);
        Assert.Equal(2, carrinho.Itens[0].ProdutoId);
    }
}
