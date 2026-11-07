using AtelieMarylu.Domain.Catalog;
using AtelieMarylu.Domain;
using AtelieMarylu.Domain.Shipping;
using AtelieMarylu.Domain.Payments;
using AtelieMarylu.Domain.Orders;

namespace AtelieMarylu.Tests;

public class OrderTests
{
    private static readonly DateTime Now = new(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);

    private static Order NewOrder(decimal shipping = 20m, decimal pixDiscount = 0) => new(
        "cliente-1",
        [new OrderItem(1, "Bolsa", 89.90m, 2), new OrderItem(2, "Necessaire", 39.90m, 1)],
        new ShippingAddress { PostalCode = "20000000", Street = "Rua A", Number = "1", District = "Centro", City = "Rio de Janeiro", State = "RJ" },
        new ShippingOption("pac", "PAC", shipping, 8),
        Now.AddHours(2),
        pixDiscount);

    [Fact]
    public void Total_sums_items_and_shipping()
    {
        var order = NewOrder();

        Assert.Equal(219.70m, order.Subtotal);
        Assert.Equal(239.70m, order.Total);
        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
    }

    [Fact]
    public void Public_code_has_friendly_format()
    {
        var code = NewOrder().Code;

        Assert.Matches("^AML-[2-9A-HJ-NP-Z]{6}$", code);
        Assert.NotEqual(code, NewOrder().Code);
    }

    [Fact]
    public void Pix_discount_applies_only_to_products()
    {
        var order = NewOrder(shipping: 20m, pixDiscount: 5);

        Assert.Equal(10.99m, order.PixDiscount);                          // 5% de 219,70 = 10,985 -> 10,99
        Assert.Equal(228.71m, order.AmountFor(PaymentMethod.Pix));      // 219,70 - 10,99 + 20
        Assert.Equal(239.70m, order.AmountFor(PaymentMethod.CreditCard));
    }

    [Fact]
    public void Payment_uses_method_amount()
    {
        var order = NewOrder(pixDiscount: 5);

        Assert.Equal(228.71m, order.StartPayment(PaymentMethod.Pix, "simulado").Amount);
        Assert.Equal(239.70m, order.StartPayment(PaymentMethod.CreditCard, "simulado").Amount);
    }

    [Fact]
    public void Order_without_items_cannot_be_created()
    {
        Assert.Throws<OrderRuleException>(() => new Order("c", [], new ShippingAddress(), new ShippingOption("x", "X", 0, null), Now));
    }

    [Fact]
    public void Confirming_payment_changes_status_to_paid()
    {
        var order = NewOrder();
        var payment = order.StartPayment(PaymentMethod.Pix, "simulado");

        order.ConfirmPayment(payment);

        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(PaymentStatus.Approved, payment.Status);
        Assert.NotNull(order.PaidAt);
    }

    [Fact]
    public void Repeated_webhook_does_not_throw()
    {
        var order = NewOrder();
        var payment = order.StartPayment(PaymentMethod.Pix, "simulado");
        order.ConfirmPayment(payment);

        order.ConfirmPayment(payment);

        Assert.Equal(OrderStatus.Paid, order.Status);
    }

    [Fact]
    public void New_payment_cancels_previous_pending()
    {
        var order = NewOrder();
        var first = order.StartPayment(PaymentMethod.CreditCard, "simulado");

        var second = order.StartPayment(PaymentMethod.Pix, "simulado");

        Assert.Equal(PaymentStatus.Cancelled, first.Status);
        Assert.Equal(PaymentStatus.Pending, second.Status);
    }

    [Fact]
    public void Editing_items_invalidates_pending_payment()
    {
        var order = NewOrder();
        var payment = order.StartPayment(PaymentMethod.Pix, "simulado");

        order.ChangeQuantity(1, 1);

        Assert.Equal(PaymentStatus.Cancelled, payment.Status);
    }

    [Fact]
    public void Paying_old_amount_after_edit_is_rejected()
    {
        var order = NewOrder();
        var oldPayment = order.StartPayment(PaymentMethod.Pix, "simulado");
        order.AddItem(new OrderItem(3, "Porta-moedas", 24.90m, 1));

        var isError = Assert.Throws<OrderRuleException>(() => order.ConfirmPayment(oldPayment));

        Assert.Contains("diferente", isError.Message);
        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
    }

    [Fact]
    public void Amount_paid_reported_by_provider_is_checked()
    {
        var order = NewOrder();
        var payment = order.StartPayment(PaymentMethod.Pix, "simulado");

        Assert.Throws<OrderRuleException>(() => order.ConfirmPayment(payment, amountPaid: 1m));
        order.ConfirmPayment(payment, amountPaid: payment.Amount);
        Assert.Equal(OrderStatus.Paid, order.Status);
    }

    [Fact]
    public void Payment_deadline_expires()
    {
        var order = NewOrder();

        Assert.False(order.IsPaymentOverdue(Now.AddHours(1)));
        Assert.True(order.IsPaymentOverdue(Now.AddHours(3)));
    }

    [Fact]
    public void Full_flow_until_delivery()
    {
        var order = NewOrder();
        order.ConfirmPayment(order.StartPayment(PaymentMethod.Pix, "simulado"));

        order.StartPreparation();
        order.MarkShipped(" BR123 ");
        order.MarkDelivered();

        Assert.Equal(OrderStatus.Delivered, order.Status);
        Assert.Equal("BR123", order.TrackingCode);
        Assert.False(order.IsPaymentOverdue(Now.AddDays(10)));
    }

    [Fact]
    public void Does_not_ship_unpaid_order()
    {
        var order = NewOrder();

        Assert.Throws<OrderRuleException>(() => order.MarkShipped("BR1"));
    }

    [Fact]
    public void Shipped_order_cannot_be_cancelled()
    {
        var order = NewOrder();
        order.ConfirmPayment(order.StartPayment(PaymentMethod.Pix, "simulado"));
        order.MarkShipped(null);

        Assert.False(order.CanCancel);
        Assert.Throws<OrderRuleException>(() => order.Cancel());
    }

    [Fact]
    public void Cancel_cancels_pending_payment_and_keeps_reason()
    {
        var order = NewOrder();
        var payment = order.StartPayment(PaymentMethod.Pix, "simulado");

        order.Cancel(" Cliente desistiu ");

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(PaymentStatus.Cancelled, payment.Status);
        Assert.Equal("Cliente desistiu", order.CancellationReason);
    }

    [Fact]
    public void Payment_on_cancelled_order_is_rejected()
    {
        var order = NewOrder();
        var payment = order.StartPayment(PaymentMethod.Pix, "simulado");
        order.Cancel();

        Assert.Throws<OrderRuleException>(() => order.ConfirmPayment(payment));
    }

    [Fact]
    public void Changing_quantity_returns_stock_difference()
    {
        var order = NewOrder();

        Assert.Equal(3, order.ChangeQuantity(1, 5));
        Assert.Equal(-1, order.ChangeQuantity(2, 0));
        Assert.Single(order.Items);
    }

    [Fact]
    public void Does_not_remove_last_item()
    {
        var order = NewOrder();
        order.ChangeQuantity(2, 0);

        Assert.Throws<OrderRuleException>(() => order.ChangeQuantity(1, 0));
    }

    [Fact]
    public void Items_do_not_change_after_payment()
    {
        var order = NewOrder();
        order.ConfirmPayment(order.StartPayment(PaymentMethod.Pix, "simulado"));

        Assert.Throws<OrderRuleException>(() => order.ChangeQuantity(1, 1));
        Assert.Throws<OrderRuleException>(() => order.AddItem(new OrderItem(3, "X", 1m, 1)));
    }

    [Fact]
    public void Adding_existing_item_sums_quantity()
    {
        var order = NewOrder();

        order.AddItem(new OrderItem(1, "Bolsa", 89.90m, 1));

        Assert.Equal(3, order.Items.First(i => i.ProductId == 1).Quantity);
    }

    [Theory]
    [InlineData("Bolsa de Tecido Floral", "bolsa-de-tecido-floral")]
    [InlineData("Necessaire média (22 x 14)", "necessaire-media-22-x-14")]
    [InlineData("  Ação & Coração!! ", "acao-coracao")]
    public void Slug_for_friendly_url(string name, string expected)
    {
        Assert.Equal(expected, SlugGenerator.Create(name));
    }
}
