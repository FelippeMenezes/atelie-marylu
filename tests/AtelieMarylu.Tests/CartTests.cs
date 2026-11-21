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

    // AML-0037: o checkout tira do carrinho só o que virou pedido
    [Fact]
    public void Removing_ordered_items_empties_the_cart_when_nothing_changed()
    {
        var cart = new Cart([new CartItem(1, 2), new CartItem(2, 1)]);

        cart.RemoveOrdered([new CartItem(1, 2), new CartItem(2, 1)]);

        Assert.True(cart.IsEmpty);
    }

    [Fact]
    public void Product_added_after_the_order_stays_in_the_cart()
    {
        var cart = new Cart([new CartItem(1, 1), new CartItem(2, 1)]);

        cart.RemoveOrdered([new CartItem(1, 1)]);

        Assert.Equal([new CartItem(2, 1)], cart.Items);
    }

    [Fact]
    public void Units_added_after_the_order_stay_in_the_cart()
    {
        var cart = new Cart([new CartItem(1, 3)]);

        cart.RemoveOrdered([new CartItem(1, 1)]);

        Assert.Equal([new CartItem(1, 2)], cart.Items);
    }

    [Fact]
    public void Removing_more_than_the_cart_has_removes_the_item_without_going_negative()
    {
        var cart = new Cart([new CartItem(1, 1)]);

        cart.RemoveOrdered([new CartItem(1, 3)]);

        Assert.True(cart.IsEmpty);
    }

    [Fact]
    public void Item_already_removed_in_another_tab_does_not_come_back()
    {
        var cart = new Cart([new CartItem(2, 1)]);

        cart.RemoveOrdered([new CartItem(1, 1)]);

        Assert.Equal([new CartItem(2, 1)], cart.Items);
    }
}
