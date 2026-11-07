using AtelieMarylu.Domain.Shipping;
using AtelieMarylu.Domain.Payments;

namespace AtelieMarylu.Domain.Orders;

public class Order
{
    private readonly List<OrderItem> _items = [];
    private readonly List<Payment> _payments = [];

    private Order() { } // EF Core

    public Order(string customerId, IEnumerable<OrderItem> items, ShippingAddress address, ShippingOption shipping,
        DateTime payBy, decimal pixDiscountPercent = 0, string? createdBy = null)
    {
        if (pixDiscountPercent is < 0 or >= 100) throw new OrderRuleException("Desconto PIX inválido.");
        CustomerId = customerId;
        _items.AddRange(items);
        if (_items.Count == 0) throw new OrderRuleException("O pedido precisa ter pelo menos um item.");
        Address = address;
        SetShipping(shipping);
        PayBy = payBy;
        PixDiscountPercent = pixDiscountPercent;
        CreatedBy = createdBy;
    }

    public int Id { get; private set; }

    // Código mostrado ao cliente; o Id interno não aparece para ele
    public string Code { get; private set; } = OrderCodeGenerator.Create();

    public string CustomerId { get; private set; } = "";
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;
    public ShippingAddress Address { get; private set; } = new();

    public string ShippingCode { get; private set; } = "";
    public string ShippingName { get; private set; } = "";
    public decimal ShippingCost { get; private set; }
    public int? ShippingDays { get; private set; }
    public string? TrackingCode { get; private set; }

    // Desconto sobre os produtos (não sobre o frete) quando o pagamento é por PIX
    public decimal PixDiscountPercent { get; private set; }

    public string? Notes { get; set; }

    // Preenchido quando um funcionário/admin cria o pedido pelo painel
    public string? CreatedBy { get; private set; }

    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    // Sem pagamento até aqui, o pedido é cancelado e o estoque volta para a loja
    public DateTime PayBy { get; private set; }
    public DateTime? PaidAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    public string? CancellationReason { get; private set; }

    public IReadOnlyList<OrderItem> Items => _items;
    public IReadOnlyList<Payment> Payments => _payments;

    public decimal Subtotal => _items.Sum(i => i.Subtotal);
    public decimal Total => Subtotal + ShippingCost;
    public decimal PixDiscount => PixDiscountCalculator.Calculate(Subtotal, PixDiscountPercent);

    public decimal AmountFor(PaymentMethod method) => method == PaymentMethod.Pix ? Total - PixDiscount : Total;

    public bool CanChangeItems => Status == OrderStatus.AwaitingPayment;
    public bool CanCancel => Status is OrderStatus.AwaitingPayment or OrderStatus.Paid or OrderStatus.Preparing;
    public bool CanChangeDelivery => Status is not (OrderStatus.Shipped or OrderStatus.Delivered or OrderStatus.Cancelled);
    public bool IsPaymentOverdue(DateTime utcNow) => Status == OrderStatus.AwaitingPayment && utcNow > PayBy;

    public void SetShipping(ShippingOption shipping)
    {
        if (!CanChangeItems) throw new OrderRuleException("O frete só pode ser alterado antes do pagamento.");
        if (shipping.Amount < 0) throw new OrderRuleException("O frete não pode ser negativo.");
        var amountChanged = shipping.Amount != ShippingCost;
        ShippingCode = shipping.Code;
        ShippingName = shipping.Name;
        ShippingCost = shipping.Amount;
        ShippingDays = shipping.DeliveryDays;
        if (amountChanged) CancelPendingPayments();
    }

    public void ChangeAddress(ShippingAddress address)
    {
        if (!CanChangeDelivery) throw new OrderRuleException("O endereço não pode mais ser alterado.");
        Address = address;
    }

    // Retorna a diferença de quantidade (positiva = precisa baixar estoque; negativa = devolver)
    public int ChangeQuantity(int productId, int newQuantity)
    {
        if (!CanChangeItems) throw new OrderRuleException("Os itens só podem ser alterados antes do pagamento.");
        var item = _items.FirstOrDefault(i => i.ProductId == productId)
            ?? throw new OrderRuleException("Item não encontrado no pedido.");

        int difference;
        if (newQuantity <= 0)
        {
            if (_items.Count == 1) throw new OrderRuleException("O pedido precisa ter pelo menos um item. Cancele o pedido.");
            _items.Remove(item);
            difference = -item.Quantity;
        }
        else
        {
            difference = newQuantity - item.Quantity;
            item.Quantity = newQuantity;
        }

        if (difference != 0) CancelPendingPayments();
        return difference;
    }

    public void AddItem(OrderItem isNew)
    {
        if (!CanChangeItems) throw new OrderRuleException("Os itens só podem ser alterados antes do pagamento.");
        var existing = _items.FirstOrDefault(i => i.ProductId == isNew.ProductId);
        if (existing is not null) existing.Quantity += isNew.Quantity;
        else _items.Add(isNew);
        CancelPendingPayments();
    }

    public Payment StartPayment(PaymentMethod method, string provider)
    {
        if (Status != OrderStatus.AwaitingPayment)
            throw new OrderRuleException("Este pedido não está aguardando pagamento.");

        CancelPendingPayments();
        var payment = new Payment(method, AmountFor(method), provider);
        _payments.Add(payment);
        return payment;
    }

    // Chamado pelo webhook do provedor (ou pelo admin, para pagamento manual)
    public void ConfirmPayment(Payment payment, decimal? amountPaid = null)
    {
        if (!_payments.Contains(payment)) throw new OrderRuleException("Pagamento não pertence a este pedido.");
        // Webhooks podem chegar repetidos: confirmar duas vezes não deve dar erro
        if (payment.Status == PaymentStatus.Approved && Status != OrderStatus.AwaitingPayment) return;
        if (Status != OrderStatus.AwaitingPayment)
            throw new OrderRuleException($"Pedido está {Describe(Status)} e não pode receber pagamento.");

        // Protege contra pagar um valor antigo (ex.: PIX gerado antes de o pedido ser editado)
        var expected = AmountFor(payment.Method);
        if (payment.Amount != expected || (amountPaid is not null && amountPaid != payment.Amount))
            throw new OrderRuleException($"Valor pago ({(amountPaid ?? payment.Amount):C}) diferente do valor do pedido ({expected:C}).");

        payment.UpdateStatus(PaymentStatus.Approved);
        Status = OrderStatus.Paid;
        PaidAt = DateTime.UtcNow;
    }

    public void StartPreparation()
    {
        if (Status != OrderStatus.Paid) throw new OrderRuleException("Só pedidos pagos podem entrar em preparo.");
        Status = OrderStatus.Preparing;
    }

    public void MarkShipped(string? trackingCode)
    {
        if (Status is not (OrderStatus.Paid or OrderStatus.Preparing))
            throw new OrderRuleException("Só pedidos pagos ou em preparo podem ser enviados.");
        TrackingCode = string.IsNullOrWhiteSpace(trackingCode) ? null : trackingCode.Trim();
        Status = OrderStatus.Shipped;
    }

    public void MarkDelivered()
    {
        if (Status != OrderStatus.Shipped) throw new OrderRuleException("Só pedidos enviados podem ser marcados como entregues.");
        Status = OrderStatus.Delivered;
    }

    public void Cancel(string? reason = null)
    {
        if (!CanCancel) throw new OrderRuleException("Este pedido não pode mais ser cancelado.");
        CancelPendingPayments();
        Status = OrderStatus.Cancelled;
        CancelledAt = DateTime.UtcNow;
        CancellationReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        // Pagamento já aprovado precisa de estorno no provedor; isso entra na fase de pagamentos.
    }

    private void CancelPendingPayments()
    {
        foreach (var pendingPayment in _payments.Where(p => p.Status == PaymentStatus.Pending))
            pendingPayment.UpdateStatus(PaymentStatus.Cancelled);
    }

    public static string Describe(OrderStatus status) => status switch
    {
        OrderStatus.AwaitingPayment => "Aguardando pagamento",
        OrderStatus.Paid => "Pago",
        OrderStatus.Preparing => "Em preparo",
        OrderStatus.Shipped => "Enviado",
        OrderStatus.Delivered => "Entregue",
        OrderStatus.Cancelled => "Cancelado",
        _ => status.ToString(),
    };
}
