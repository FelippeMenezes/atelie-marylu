namespace AtelieMarylu.Domain.Carrinho;

// O carrinho guarda só ProdutoId e Quantidade. Preço e nome são sempre lidos do banco,
// para o cliente não conseguir alterar o valor editando o navegador.
public record ItemCarrinho(int ProdutoId, int Quantidade);

public class Carrinho
{
    public const int QuantidadeMaximaPorItem = 20;

    private readonly List<ItemCarrinho> _itens = [];

    public Carrinho() { }

    public Carrinho(IEnumerable<ItemCarrinho> itens)
    {
        foreach (var item in itens)
            Adicionar(item.ProdutoId, item.Quantidade);
    }

    public IReadOnlyList<ItemCarrinho> Itens => _itens;
    public int TotalDeUnidades => _itens.Sum(i => i.Quantidade);
    public bool Vazio => _itens.Count == 0;

    public void Adicionar(int produtoId, int quantidade = 1)
    {
        if (quantidade <= 0) return;
        var atual = _itens.FirstOrDefault(i => i.ProdutoId == produtoId)?.Quantidade ?? 0;
        AlterarQuantidade(produtoId, atual + quantidade);
    }

    public void AlterarQuantidade(int produtoId, int quantidade)
    {
        var indice = _itens.FindIndex(i => i.ProdutoId == produtoId);
        if (quantidade <= 0)
        {
            if (indice >= 0) _itens.RemoveAt(indice);
            return;
        }

        var item = new ItemCarrinho(produtoId, Math.Min(quantidade, QuantidadeMaximaPorItem));
        if (indice >= 0) _itens[indice] = item;
        else _itens.Add(item);
    }

    public void Remover(int produtoId) => AlterarQuantidade(produtoId, 0);

    public void Limpar() => _itens.Clear();
}
