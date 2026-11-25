using AtelieMarylu.Domain.Orders;

namespace AtelieMarylu.Web.Services;

// Resultado do "Cancelar pedido" da cliente: cancelado na hora (não pago) ou solicitação registrada (pago).
// Leva o que os e-mails de confirmação precisam, lido dentro da mesma transação: depois da gravação,
// enviar o e-mail não depende de outra consulta ao banco (que poderia falhar com a solicitação já gravada).
public record CancellationOutcome(int OrderId, string OrderCode, string? CustomerEmail, string? CustomerName, bool CancelledNow,
    string? Protocol, DateTime At, OrderStatus OrderStatusAtRequest, string? Reason);

// Uma linha da lista "Pedidos de cancelamento sem resposta" do painel
public record PendingCancellation(int OrderId, string OrderCode, string Protocol, DateTime RequestedAt, OrderStatus OrderStatusAtRequest);
