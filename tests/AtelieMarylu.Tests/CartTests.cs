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

    [Fact]
    public void RoomFor_product_not_in_cart_is_the_stock()
    {
        Assert.Equal(3, new Cart().RoomFor(1, stock: 3));
    }

    [Fact]
    public void RoomFor_discounts_what_is_already_in_the_cart()
    {
        var cart = new Cart([new CartItem(1, 2)]);

        Assert.Equal(1, cart.RoomFor(1, stock: 3));
    }

    [Fact]
    public void RoomFor_sold_out_product_is_zero()
    {
        Assert.Equal(0, new Cart().RoomFor(1, stock: 0));
    }

    [Fact]
    public void RoomFor_is_never_negative_when_stock_dropped_below_the_cart()
    {
        var cart = new Cart([new CartItem(1, 3)]); // outra cliente comprou depois que estas 3 entraram

        Assert.Equal(0, cart.RoomFor(1, stock: 1));
    }

    [Fact]
    public void RoomFor_respects_the_limit_per_item()
    {
        var cart = new Cart([new CartItem(1, Cart.MaxQuantityPerItem - 1)]);

        Assert.Equal(1, cart.RoomFor(1, stock: 100));
    }

    [Fact]
    public void RoomFor_only_looks_at_the_given_product()
    {
        var cart = new Cart([new CartItem(2, 5)]);

        Assert.Equal(3, cart.RoomFor(1, stock: 3));
    }

    // Cart.AddWithinStock: quanto entrou e o motivo, que decide a mensagem da tela (AML-0038)
    [Theory]
    [InlineData(0, 2, 5, 2, AddWithinStockOutcome.Added)]
    [InlineData(0, 1, 0, 0, AddWithinStockOutcome.SoldOut)]
    [InlineData(2, 1, 0, 0, AddWithinStockOutcome.SoldOut)]          // esgotado vence "já está no carrinho"
    [InlineData(1, 3, 3, 2, AddWithinStockOutcome.Partial)]
    [InlineData(2, 1, 2, 0, AddWithinStockOutcome.CartHasAllStock)]
    [InlineData(3, 1, 1, 0, AddWithinStockOutcome.CartAboveStock)]   // outra cliente comprou depois que estas 3 entraram
    [InlineData(19, 5, 100, 1, AddWithinStockOutcome.LimitPerItem)]  // havia estoque: quem limitou foi o máximo por item
    [InlineData(20, 1, 100, 0, AddWithinStockOutcome.LimitPerItem)]
    [InlineData(18, 5, 20, 2, AddWithinStockOutcome.Partial)]        // estoque e limite iguais: é falta de estoque
    public void AddWithinStock_says_how_many_went_in_and_why(int inCart, int requested, int stock, int expectedAdded, AddWithinStockOutcome expected)
    {
        var cart = new Cart(inCart > 0 ? [new CartItem(1, inCart)] : []);

        var result = cart.AddWithinStock(1, requested, stock);

        Assert.Equal(new AddWithinStockResult(expectedAdded, expected), result);
        Assert.Equal(inCart + expectedAdded, cart.TotalUnits);
    }
}
