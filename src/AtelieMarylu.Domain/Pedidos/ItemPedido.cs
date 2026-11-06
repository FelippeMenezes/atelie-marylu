namespace AtelieMarylu.Domain.Pedidos;

// Nome e preço são copiados do produto no momento da compra:
// se o preço mudar depois, o pedido continua com o valor que o cliente pagou.
public class ItemPedido
{
    private ItemPedido() { } // EF Core

    public ItemPedido(int produtoId, string nomeProduto, decimal precoUnitario, int quantidade)
    {
        if (quantidade <= 0) throw new RegraDePedidoException("A quantidade deve ser maior que zero.");
        ProdutoId = produtoId;
        NomeProduto = nomeProduto;
        PrecoUnitario = precoUnitario;
        Quantidade = quantidade;
    }

    public int Id { get; private set; }
    public int ProdutoId { get; private set; }
    public string NomeProduto { get; private set; } = "";
    public decimal PrecoUnitario { get; private set; }
    public int Quantidade { get; internal set; }
    public decimal Subtotal => PrecoUnitario * Quantidade;
}
