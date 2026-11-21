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
