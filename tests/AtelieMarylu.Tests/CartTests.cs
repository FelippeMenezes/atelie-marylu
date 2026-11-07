using AtelieMarylu.Domain.Carts;

namespace AtelieMarylu.Tests;

public class CartTests
{
    [Fact]
    public void Adding_same_product_sums_quantity()
    {
        var cart = new Cart();

        cart.Add(1, 2);
        cart.Add(1, 3);

        Assert.Single(cart.Items);
        Assert.Equal(5, cart.TotalUnits);
    }

    [Fact]
    public void Zero_quantity_removes_item()
    {
        var cart = new Cart();
        cart.Add(1);

        cart.ChangeQuantity(1, 0);

        Assert.True(cart.IsEmpty);
    }

    [Fact]
    public void Quantity_has_limit_per_item()
    {
        var cart = new Cart();

        cart.Add(1, 999);

        Assert.Equal(Cart.MaxQuantityPerItem, cart.Items[0].Quantity);
    }

    [Fact]
    public void Ignores_invalid_quantities_from_browser()
    {
        var cart = new Cart([new CartItem(1, -5), new CartItem(2, 1)]);

        Assert.Single(cart.Items);
        Assert.Equal(2, cart.Items[0].ProductId);
    }
}
