using AtelieMarylu.Domain.Shipping;
using AtelieMarylu.Domain.Payments;

namespace AtelieMarylu.Domain.Orders;

public class Order
{
    private readonly List<OrderItem> _items = [];
    private readonly List<Payment> _payments = [];
    private readonly List<CancellationRequest> _cancellationRequests = [];

    private Order() { } // EF Core

    public Order(string customerId, IEnumerable<OrderItem> items, ShippingAddress address, ShippingOption shipping,
        DateTime payBy, decimal pixDiscountPercent = 0, string? createdBy = null, string? language = null)
    {
        if (pixDiscountPercent is < 0 or >= 100) throw new OrderRuleException("Invalid PIX discount.");
        CustomerId = customerId;
        _items.AddRange(items);
        if (_items.Count == 0) throw new OrderRuleException("The order must have at least one item.");
        EnsureSafeForEmail(address);
        Address = address;
        SetShipping(shipping);
        PayBy = payBy;
        PixDiscountPercent = pixDiscountPercent;
        CreatedBy = createdBy;
        Language = language;
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

    // Idioma em que a cliente fez o pedido (ex.: "es"). Os e-mails do pedido saem nele, inclusive os que nascem
    // fora de uma tela (o webhook do pagamento não tem o cookie de idioma). Null = idioma padrão da loja.
    public string? Language { get; private set; }

    // Sem pagamento até aqui, o pedido é cancelado e o estoque volta para a loja
    public DateTime PayBy { get; private set; }
    public DateTime? PaidAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    public string? CancellationReason { get; private set; }

    public IReadOnlyList<OrderItem> Items => _items;
    public IReadOnlyList<Payment> Payments => _payments;
    public IReadOnlyList<CancellationRequest> CancellationRequests => _cancellationRequests;

    // A solicitação da cliente que a equipe ainda não respondeu (no máximo uma por vez)
    public CancellationRequest? PendingCancellationRequest => _cancellationRequests.FirstOrDefault(r => r.IsPending);

    public decimal Subtotal => _items.Sum(i => i.Subtotal);
    public decimal Total => Subtotal + ShippingCost;
    public decimal PixDiscount => PixDiscountCalculator.Calculate(Subtotal, PixDiscountPercent);

    public decimal AmountFor(PaymentMethod method) => method == PaymentMethod.Pix ? Total - PixDiscount : Total;

    public bool CanChangeItems => Status == OrderStatus.AwaitingPayment;
    public bool CanCancel => Status is OrderStatus.AwaitingPayment or OrderStatus.Paid or OrderStatus.Preparing;
    public bool CanChangeDelivery => Status is not (OrderStatus.Shipped or OrderStatus.Delivered or OrderStatus.Cancelled);
    // Pago e ainda não enviado. A mesma regra decide o MarkShipped e o que o painel manda a Marylu fazer com o pacote
    public bool CanShip => Status is OrderStatus.Paid or OrderStatus.Preparing;
    public bool IsPaymentOverdue(DateTime utcNow) => Status == OrderStatus.AwaitingPayment && utcNow > PayBy;

    // O endereço vai nos e-mails do pedido. Sem link, e-mail nem telefone nos campos de texto livre, ninguém usa o
    // domínio da loja para mandar golpe a quem teve o e-mail cadastrado por outra pessoa (AML-0042). As telas já
    // validam com a mesma regra (CustomerText); aqui é a garantia, para qualquer caminho que crie ou edite o pedido.
    private static void EnsureSafeForEmail(ShippingAddress address)
    {
        if (!address.IsSafeForEmail())
            throw new OrderRuleException("Remove links, emails and phone numbers from the address (the contact phone number goes in the account).");
    }

    public void SetShipping(ShippingOption shipping)
    {
        if (!CanChangeItems) throw new OrderRuleException("Shipping can only be changed before payment.");
        if (shipping.Amount < 0) throw new OrderRuleException("Shipping cannot be negative.");
        var amountChanged = shipping.Amount != ShippingCost;
        ShippingCode = shipping.Code;
        ShippingName = shipping.Name;
        ShippingCost = shipping.Amount;
        ShippingDays = shipping.DeliveryDays;
        if (amountChanged) CancelPendingPayments();
    }

    public void ChangeAddress(ShippingAddress address)
    {
        if (!CanChangeDelivery) throw new OrderRuleException("The address can no longer be changed.");
        EnsureSafeForEmail(address);
        Address = address;
    }

    // Retorna a diferença de quantidade (positiva = precisa baixar estoque; negativa = devolver)
    public int ChangeQuantity(int productId, int newQuantity)
    {
        if (!CanChangeItems) throw new OrderRuleException("Items can only be changed before payment.");
        var item = _items.FirstOrDefault(i => i.ProductId == productId)
            ?? throw new OrderRuleException("Item not found in the order.");

        int difference;
        if (newQuantity <= 0)
        {
            if (_items.Count == 1) throw new OrderRuleException("The order must have at least one item. Cancel the order instead.");
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
        if (!CanChangeItems) throw new OrderRuleException("Items can only be changed before payment.");
        var existing = _items.FirstOrDefault(i => i.ProductId == isNew.ProductId);
        if (existing is not null) existing.Quantity += isNew.Quantity;
        else _items.Add(isNew);
        CancelPendingPayments();
    }

    public Payment StartPayment(PaymentMethod method, string provider)
    {
        if (Status != OrderStatus.AwaitingPayment)
            throw new OrderRuleException("This order is not awaiting payment.");

        CancelPendingPayments();
        var payment = new Payment(method, AmountFor(method), provider);
        _payments.Add(payment);
        return payment;
    }

    // Chamado pelo webhook do provedor (ou pelo admin, para pagamento manual)
    public void ConfirmPayment(Payment payment, decimal? amountPaid = null)
    {
        if (!_payments.Contains(payment)) throw new OrderRuleException("The payment does not belong to this order.");
        // Webhooks podem chegar repetidos: confirmar duas vezes não deve dar erro
        if (payment.Status == PaymentStatus.Approved && Status != OrderStatus.AwaitingPayment) return;
        if (Status != OrderStatus.AwaitingPayment)
            throw new OrderRuleException("The order is {0} and cannot receive payment.", Status);

        // Protege contra pagar um valor antigo (ex.: PIX gerado antes de o pedido ser editado)
        var expected = AmountFor(payment.Method);
        if (payment.Amount != expected || (amountPaid is not null && amountPaid != payment.Amount))
            throw new OrderRuleException("Amount paid ({0}) differs from the order amount ({1}).", (amountPaid ?? payment.Amount).ToBrl(), expected.ToBrl());

        payment.UpdateStatus(PaymentStatus.Approved);
        // Pago: as outras cobranças abertas deixam de valer, para a cliente não pagar duas vezes
        CancelPendingPayments();
        Status = OrderStatus.Paid;
        PaidAt = DateTime.UtcNow;
    }

    // Chamado pelo webhook quando o provedor avisa que o dinheiro voltou para a cliente:
    // estorno feito pela loja no painel do provedor ou contestação da cliente no cartão (chargeback).
    public PaymentReversal RegisterReversal(Payment payment)
    {
        if (!_payments.Contains(payment)) throw new OrderRuleException("The payment does not belong to this order.");
        var wasApproved = payment.Status == PaymentStatus.Approved;
        payment.UpdateStatus(PaymentStatus.Refunded);
        if (!wasApproved) return PaymentReversal.NothingToDo;

        // Outro pagamento aprovado continua cobrindo o pedido (ex.: a cliente pagou duas vezes)
        if (_payments.Any(p => p != payment && p.Status == PaymentStatus.Approved && p.Amount == payment.Amount))
            return PaymentReversal.StillPaid;

        if (Status is OrderStatus.Shipped or OrderStatus.Delivered)
            return PaymentReversal.AlreadyShipped;

        // Pago ou em preparo: o pedido não pode mais sair sem o dinheiro
        if (Status is OrderStatus.Paid or OrderStatus.Preparing)
        {
            Cancel("Pagamento estornado ou contestado");
            return PaymentReversal.OrderCancelled;
        }

        // Já cancelado (ex.: a loja estornou um pagamento que chegou depois do prazo)
        return PaymentReversal.NothingToDo;
    }

    public void StartPreparation()
    {
        if (Status != OrderStatus.Paid) throw new OrderRuleException("Only paid orders can go into preparation.");
        Status = OrderStatus.Preparing;
    }

    public void MarkShipped(string? trackingCode)
    {
        if (!CanShip)
            throw new OrderRuleException("Only paid or in-preparation orders can be shipped.");
        TrackingCode = string.IsNullOrWhiteSpace(trackingCode) ? null : trackingCode.Trim();
        Status = OrderStatus.Shipped;
    }

    public void MarkDelivered()
    {
        if (Status != OrderStatus.Shipped) throw new OrderRuleException("Only shipped orders can be marked as delivered.");
        Status = OrderStatus.Delivered;
    }

    public void Cancel(string? reason = null)
    {
        if (!CanCancel) throw new OrderRuleException("This order can no longer be cancelled.");
        CancelPendingPayments();
        Status = OrderStatus.Cancelled;
        CancelledAt = DateTime.UtcNow;
        CancellationReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        // Cancelar o pedido responde a solicitação da cliente que estiver aberta.
        // Pagamento já aprovado continua precisando de estorno no provedor (o painel avisa).
        PendingCancellationRequest?.Resolve(CancellationReason, CancelledAt.Value);
    }

    // A cliente pede, em "Meus pedidos", para cancelar um pedido pago ou desistir de um pedido recebido.
    // Só registra o pedido: a equipe é avisada, combina a devolução da peça e faz o estorno no provedor.
    // (Pedido ainda não pago não passa por aqui: o OrderService cancela na hora com Cancel.)
    //
    // Sem prazo depois da entrega, de propósito: a loja não guarda a data em que a cliente recebeu a peça
    // ("Entregue" é marcado à mão), e o arrependimento de 7 dias conta do recebimento (CDC, art. 49).
    // Recusar no código por um dado impreciso negaria um direito legítimo. Por isso o sistema sempre aceita
    // e registra a solicitação, e a equipe avalia o prazo ao responder (é o padrão das lojas grandes).
    // O utcNow fica na assinatura para quando houver data de entrega confiável (ex.: rastreio do Melhor Envio).
    public bool CanRequestCancellation(DateTime utcNow) =>
        Status is OrderStatus.Paid or OrderStatus.Preparing or OrderStatus.Shipped or OrderStatus.Delivered
        && PendingCancellationRequest is null;

    public CancellationRequest RequestCancellation(string? reason, DateTime utcNow)
    {
        if (!CanRequestCancellation(utcNow))
            throw new OrderRuleException("This order can no longer receive a cancellation request.");
        var request = new CancellationRequest(Status, reason, utcNow);
        _cancellationRequests.Add(request);
        return request;
    }

    // A equipe responde a solicitação sem cancelar o pedido (ex.: a peça já foi enviada e o estorno
    // foi feito depois da devolução, ou a cliente desistiu de cancelar).
    public void ResolveCancellationRequest(string? note, DateTime utcNow)
    {
        var request = PendingCancellationRequest
            ?? throw new OrderRuleException("This order has no open cancellation request.");
        request.Resolve(note, utcNow);
    }

    private void CancelPendingPayments()
    {
        foreach (var pendingPayment in _payments.Where(p => p.Status == PaymentStatus.Pending))
            pendingPayment.UpdateStatus(PaymentStatus.Cancelled);
    }

    public static string Describe(OrderStatus status) => status switch
    {
        OrderStatus.AwaitingPayment => "Awaiting payment",
        OrderStatus.Paid => "Paid",
        OrderStatus.Preparing => "In preparation",
        OrderStatus.Shipped => "Shipped",
        OrderStatus.Delivered => "Delivered",
        OrderStatus.Cancelled => "Cancelled",
        _ => status.ToString(),
    };
}
