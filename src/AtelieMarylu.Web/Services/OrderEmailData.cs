using AtelieMarylu.Domain.Orders;
using AtelieMarylu.Domain.Shipping;

namespace AtelieMarylu.Web.Services;

// Tipos de e-mail da outbox (coluna OutboxEmail.Kind). Gravados no banco: renomear um exige migrar as linhas pendentes.
public static class OutboxEmailKinds
{
    public const string OrderReceived = "order.received";
    public const string PaymentConfirmed = "order.paid";
    public const string CancellationReceived = "cancellation.customer";
    public const string CancellationStaffNotice = "cancellation.store";
}

// "Foto" do pedido no momento do evento, gravada em JSON na outbox. O e-mail é montado a partir dela, e não do
// pedido atual: se a equipe mudar o pedido entre o evento e o envio, o e-mail continua contando o que aconteceu.
// Só leva o que o e-mail mostra (minimização de dados, LGPD art. 6º, III).
public sealed record OrderEmailData(
    int OrderId,
    string OrderCode,
    string? CustomerName,
    DateTime At,
    IReadOnlyList<OrderEmailLine> Items,
    decimal Subtotal,
    string ShippingName,
    decimal ShippingCost,
    decimal Total,
    decimal PixTotal,
    DateTime PayBy,
    bool IsPickup,
    string? AddressLine,
    decimal? AmountPaid)
{
    public static OrderEmailData From(Order order, string? customerName, DateTime at, decimal? amountPaid = null)
    {
        var isPickup = ShippingHandoverRules.For(order.ShippingCode) == ShippingHandover.CustomerPickup;
        var a = order.Address;
        // Pedido antigo com link, e-mail ou telefone no endereço (antes da regra do AML-0042): o e-mail sai sem a
        // linha do endereço, que continua na página do pedido. O pedido novo nem chega a ser criado assim.
        var addressLine = isPickup || !a.IsSafeForEmail() ? null
            : $"{a.Street}, {a.Number}{(string.IsNullOrWhiteSpace(a.Complement) ? "" : $" - {a.Complement}")} · {a.District} · {a.City}/{a.State} · {FormatPostalCode(a.PostalCode)}";
        return new(order.Id, order.Code, customerName, at,
            order.Items.Select(i => new OrderEmailLine(i.ProductName, i.Quantity, i.Subtotal)).ToList(),
            order.Subtotal, order.ShippingName, order.ShippingCost, order.Total, order.AmountFor(Domain.Payments.PaymentMethod.Pix),
            order.PayBy, isPickup, addressLine, amountPaid);
    }

    // O CEP fica gravado só com os 8 dígitos (PostalCode.Normalize)
    private static string FormatPostalCode(string digits) => digits.Length == 8 ? $"{digits[..5]}-{digits[5..]}" : digits;
}

public sealed record OrderEmailLine(string Name, int Quantity, decimal Subtotal);

// Solicitação de cancelamento (AML-0040). O motivo escrito pela cliente nunca entra aqui: ele não sai em e-mail.
public sealed record CancellationEmailData(
    int OrderId,
    string OrderCode,
    string? CustomerName,
    string Protocol,
    DateTime At,
    OrderStatus OrderStatusAtRequest);
