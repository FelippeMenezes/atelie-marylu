namespace AtelieMarylu.Domain.Carts;

public class Cart
{
    public const int MaxQuantityPerItem = 20;

    private readonly List<CartItem> _items = [];

    public Cart() { }

    public Cart(IEnumerable<CartItem> items)
    {
        foreach (var item in items)
            Add(item.ProductId, item.Quantity);
    }

    public IReadOnlyList<CartItem> Items => _items;
    public int TotalUnits => _items.Sum(i => i.Quantity);
    public bool IsEmpty => _items.Count == 0;

    public void Add(int productId, int quantity = 1)
    {
        if (quantity <= 0) return;
        var currentValue = _items.FirstOrDefault(i => i.ProductId == productId)?.Quantity ?? 0;
        ChangeQuantity(productId, currentValue + quantity);
    }

    public void ChangeQuantity(int productId, int quantity)
    {
        var index = _items.FindIndex(i => i.ProductId == productId);
        if (quantity <= 0)
        {
            if (index >= 0) _items.RemoveAt(index);
            return;
        }

        var item = new CartItem(productId, Math.Min(quantity, MaxQuantityPerItem));
        if (index >= 0) _items[index] = item;
        else _items.Add(item);
    }

    public void Remove(int productId) => ChangeQuantity(productId, 0);

    // Quantas unidades deste produto ainda podem entrar no carrinho, dado o estoque à venda agora.
    // Conta o que já está no carrinho (inclusive o que outra aba pôs) e o limite por item (AML-0038).
    // Nunca devolve negativo: o estoque pode ter caído abaixo do que a cliente já tem no carrinho.
    public int RoomFor(int productId, int stock)
    {
        var inCart = _items.FirstOrDefault(i => i.ProductId == productId)?.Quantity ?? 0;
        return Math.Max(0, Math.Min(stock, MaxQuantityPerItem) - inCart);
    }

    // Acrescenta só o que cabe (RoomFor) e diz o que limitou, para a tela mostrar a mensagem certa.
    // A ordem dos testes importa: "esgotado" vem antes de tudo, e o limite por item só é o motivo
    // quando o estoque, sozinho, deixaria entrar mais.
    public AddWithinStockResult AddWithinStock(int productId, int requested, int stock)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requested);
        var inCart = _items.FirstOrDefault(i => i.ProductId == productId)?.Quantity ?? 0;
        var added = Math.Min(requested, RoomFor(productId, stock));
        Add(productId, added);

        var outcome =
            added == requested ? AddWithinStockOutcome.Added
            : stock <= 0 ? AddWithinStockOutcome.SoldOut
            : stock > MaxQuantityPerItem && inCart + requested > MaxQuantityPerItem ? AddWithinStockOutcome.LimitPerItem
            : added > 0 ? AddWithinStockOutcome.Partial
            : inCart > stock ? AddWithinStockOutcome.CartAboveStock
            : AddWithinStockOutcome.CartHasAllStock;
        return new AddWithinStockResult(added, outcome);
    }

    // Tira do carrinho o que virou pedido: subtrai as quantidades pedidas e remove o que zerar.
    // O carrinho pode ter mudado em outra aba enquanto o checkout estava aberto: o que foi
    // acrescentado depois (outro produto ou mais unidades) não entrou no pedido e continua aqui.
    // O que a cliente já tinha tirado não volta (AML-0037).
    public void RemoveOrdered(IEnumerable<CartItem> ordered)
    {
        foreach (var item in ordered)
        {
            var current = _items.FirstOrDefault(i => i.ProductId == item.ProductId)?.Quantity ?? 0;
            ChangeQuantity(item.ProductId, current - item.Quantity);
        }
    }
}
