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
    public void Only_paid_or_in_preparation_orders_can_be_shipped()
    {
        // O painel só manda a Marylu levar ou entregar o pacote quando CanShip: nunca num pedido não pago ou cancelado
        var order = NewOrder();
        Assert.False(order.CanShip);                      // aguardando pagamento

        order.ConfirmPayment(order.StartPayment(PaymentMethod.Pix, "simulado"));
        Assert.True(order.CanShip);                       // pago

        order.StartPreparation();
        Assert.True(order.CanShip);                       // em preparo

        order.MarkShipped("BR123");
        Assert.False(order.CanShip);                      // enviado

        var cancelled = NewOrder();
        cancelled.Cancel();
        Assert.False(cancelled.CanShip);                  // cancelado
        Assert.Throws<OrderRuleException>(() => cancelled.MarkShipped("BR123"));
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

        Assert.Contains("differs", isError.Message);
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

    // Cancelamento pedido pela cliente (AML-0040)
    // AML-0042: o endereço vai nos e-mails do pedido. A entidade recusa link, e-mail e telefone, para qualquer
    // caminho que crie ou edite o pedido (as telas validam antes, com a mesma regra).
    [Theory]
    [InlineData("Rua A acesse golpe.com", null)]
    [InlineData("Rua A", "ligue 0800 123 4567")]
    [InlineData("Rua A", "suporte@golpe")]
    public void Address_with_link_email_or_phone_is_refused(string street, string? complement)
    {
        var address = new ShippingAddress
        {
            PostalCode = "20000000", Street = street, Number = "1", Complement = complement, District = "Centro", City = "Rio de Janeiro", State = "RJ",
        };

        var created = Assert.Throws<OrderRuleException>(() => new Order("c", [new OrderItem(1, "Bolsa", 10m, 1)], address,
            new ShippingOption("pac", "PAC", 10m, 5), Now.AddHours(2)));
        Assert.Equal("Remove links, emails and phone numbers from the address (the contact phone number goes in the account).", created.Message);

        var changed = Assert.Throws<OrderRuleException>(() => NewOrder().ChangeAddress(address));
        Assert.Equal("Remove links, emails and phone numbers from the address (the contact phone number goes in the account).", changed.Message);
    }

    private static Order PaidOrder()
    {
        var order = NewOrder();
        order.ConfirmPayment(order.StartPayment(PaymentMethod.Pix, "simulado"));
        return order;
    }

    [Fact]
    public void Cancellation_request_is_accepted_in_every_paid_stage()
    {
        var paid = PaidOrder();
        var preparing = PaidOrder();
        preparing.StartPreparation();
        var shipped = PaidOrder();
        shipped.MarkShipped("BR123");
        var delivered = PaidOrder();
        delivered.MarkShipped(null);
        delivered.MarkDelivered();

        Assert.All([paid, preparing, shipped, delivered], o => Assert.True(o.CanRequestCancellation(Now)));
    }

    [Fact]
    public void Delivered_order_accepts_request_long_after_delivery()
    {
        // A loja não sabe a data real do recebimento: quem avalia o prazo é a equipe, não o código
        var order = PaidOrder();
        order.MarkShipped(null);
        order.MarkDelivered();

        Assert.True(order.CanRequestCancellation(Now.AddYears(1)));
    }

    [Fact]
    public void Unpaid_or_cancelled_order_does_not_accept_request()
    {
        var unpaid = NewOrder();
        var cancelled = PaidOrder();
        cancelled.Cancel();

        Assert.False(unpaid.CanRequestCancellation(Now));
        Assert.False(cancelled.CanRequestCancellation(Now));
        Assert.Throws<OrderRuleException>(() => unpaid.RequestCancellation(null, Now));
    }

    [Fact]
    public void Only_one_open_request_at_a_time_and_a_new_one_after_the_answer()
    {
        var order = PaidOrder();
        var first = order.RequestCancellation("  tamanho errado  ", Now);

        Assert.Equal("tamanho errado", first.Reason);
        Assert.Equal(OrderStatus.Paid, first.OrderStatusAtRequest);
        Assert.Matches("^CAN-[2-9A-HJ-NP-Z]{6}$", first.Protocol);
        Assert.False(order.CanRequestCancellation(Now));
        Assert.Throws<OrderRuleException>(() => order.RequestCancellation(null, Now));

        order.ResolveCancellationRequest("Cliente desistiu", Now.AddHours(1));

        Assert.True(order.CanRequestCancellation(Now.AddHours(2)));
        order.RequestCancellation(null, Now.AddHours(2));
        Assert.Equal(2, order.CancellationRequests.Count);
    }

    [Fact]
    public void Reason_longer_than_limit_is_refused()
    {
        var order = PaidOrder();

        Assert.Throws<OrderRuleException>(() => order.RequestCancellation(new string('a', CancellationRequest.ReasonMaxLength + 1), Now));
        Assert.Empty(order.CancellationRequests);
    }

    [Fact]
    public void Cancelling_answers_the_open_request()
    {
        var order = PaidOrder();
        order.RequestCancellation(null, Now);

        order.Cancel("Pedido da cliente");

        Assert.Null(order.PendingCancellationRequest);
        Assert.Equal("Pedido da cliente", order.CancellationRequests.Single().ResolutionNote);
    }

    [Theory]
    [InlineData("Bolsa de Tecido Floral", "bolsa-de-tecido-floral")]
    [InlineData("Necessaire média (22 x 14)", "necessaire-media-22-x-14")]
    [InlineData("  Ação & Coração!! ", "acao-coracao")]
    public void Slug_for_friendly_url(string name, string expected)
    {
        Assert.Equal(expected, SlugGenerator.Create(name));
    }

    [Fact]
    public void Payment_that_did_not_open_is_marked_as_failed()
    {
        var order = NewOrder();
        var payment = order.StartPayment(PaymentMethod.Pix, "simulado");

        payment.MarkStartFailed();

        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
    }

    [Fact]
    public void Marking_an_approved_payment_as_failed_changes_nothing()
    {
        // Um status final nunca é sobrescrito: o dinheiro entrou
        var order = NewOrder();
        var payment = order.StartPayment(PaymentMethod.Pix, "simulado");
        order.ConfirmPayment(payment);

        payment.MarkStartFailed();

        Assert.Equal(PaymentStatus.Approved, payment.Status);
        Assert.Equal(OrderStatus.Paid, order.Status);
    }

    [Fact]
    public void Marking_a_cancelled_payment_as_failed_changes_nothing()
    {
        // Ex.: outra aba trocou de forma de pagamento enquanto o provedor falhava
        var order = NewOrder();
        var first = order.StartPayment(PaymentMethod.Pix, "simulado");
        order.StartPayment(PaymentMethod.CreditCard, "simulado");

        first.MarkStartFailed();

        Assert.Equal(PaymentStatus.Cancelled, first.Status);
    }

    [Fact]
    public void Payment_already_open_at_the_provider_cannot_be_marked_as_failed()
    {
        // Esconderia um link que a cliente ainda pode pagar: é erro de programação, não regra de negócio
        var order = NewOrder();
        var payment = order.StartPayment(PaymentMethod.CreditCard, "simulado");
        payment.RecordStart("ref-1", "https://pagamento.pagseguro.uol.com.br/pagamento?code=1", null, "CHEC_1");

        Assert.Throws<InvalidOperationException>(payment.MarkStartFailed);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
    }

    [Fact]
    public void Payment_waiting_for_the_provider_is_opening_not_failed()
    {
        // A chamada ao provedor pode estar em andamento (ex.: em outra aba): ainda não é hora de "Tentar de novo"
        var payment = NewOrder().StartPayment(PaymentMethod.Pix, "simulado");
        var now = payment.CreatedAt.AddSeconds(30);

        Assert.True(payment.IsOpening(now));
        Assert.False(payment.DidNotOpen(now));
    }

    [Fact]
    public void Payment_without_provider_answer_after_the_window_did_not_open()
    {
        // Ex.: a conexão caiu no meio da chamada e a tentativa ficou pendente sem link
        var payment = NewOrder().StartPayment(PaymentMethod.Pix, "simulado");
        var now = payment.CreatedAt + Payment.OpeningWindow + TimeSpan.FromSeconds(1);

        Assert.False(payment.IsOpening(now));
        Assert.True(payment.DidNotOpen(now));
    }

    [Fact]
    public void Failed_payment_did_not_open_right_away()
    {
        var payment = NewOrder().StartPayment(PaymentMethod.Pix, "simulado");
        payment.MarkStartFailed();

        Assert.True(payment.DidNotOpen(payment.CreatedAt));
        Assert.False(payment.IsOpening(payment.CreatedAt));
    }

    [Fact]
    public void Payment_open_at_the_provider_is_neither_opening_nor_failed()
    {
        var payment = NewOrder().StartPayment(PaymentMethod.Pix, "simulado");
        payment.RecordStart("ref-1", null, "00020126PIX", null);
        var later = payment.CreatedAt.AddHours(1);

        Assert.False(payment.IsOpening(payment.CreatedAt));
        Assert.False(payment.DidNotOpen(later));
    }
}
