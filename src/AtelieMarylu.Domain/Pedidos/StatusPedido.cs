namespace AtelieMarylu.Domain.Pedidos;

public enum StatusPedido
{
    AguardandoPagamento = 1,
    Pago = 2,
    EmPreparo = 3,
    Enviado = 4,
    Entregue = 5,
    Cancelado = 6,
}
