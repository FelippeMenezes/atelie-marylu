using AtelieMarylu.Domain.Orders;

namespace AtelieMarylu.Web.Services;

// Resultado do "Cancelar pedido" da cliente: cancelado na hora (não pago) ou solicitação registrada (pago).
// Os e-mails de confirmação já foram gravados na outbox, na mesma transação (AML-0041): a tela só mostra o resultado.
public record CancellationOutcome(int OrderId, string OrderCode, bool CancelledNow, string? Protocol, DateTime At,
    OrderStatus OrderStatusAtRequest);

// Uma linha da lista "Pedidos de cancelamento sem resposta" do painel
public record PendingCancellation(int OrderId, string OrderCode, string Protocol, DateTime RequestedAt, OrderStatus OrderStatusAtRequest);
