namespace AtelieMarylu.Domain.Orders;

// Nome e preço são copiados do produto no momento da compra:
// se o preço mudar depois, o pedido continua com o valor que o cliente pagou.
public class OrderItem
{
    private OrderItem() { } // EF Core

    public OrderItem(int productId, string productName, decimal unitPrice, int quantity)
    {
        if (quantity <= 0) throw new OrderRuleException("Quantity must be greater than zero.");
        ProductId = productId;
        ProductName = productName;
        UnitPrice = unitPrice;
        Quantity = quantity;
    }

    public int Id { get; private set; }
    public int ProductId { get; private set; }
    public string ProductName { get; private set; } = "";
    public decimal UnitPrice { get; private set; }
    public int Quantity { get; internal set; }
    public decimal Subtotal => UnitPrice * Quantity;
}
