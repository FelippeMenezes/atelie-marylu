namespace AtelieMarylu.Domain.Payments;

// O que aconteceu com o pedido quando um pagamento aprovado foi estornado ou contestado (chargeback)
public enum PaymentReversal
{
    // O pagamento não estava aprovado: só o status dele muda
    NothingToDo,

    // O pedido ainda não tinha saído: foi cancelado, e o estoque volta
    OrderCancelled,

    // O pedido já foi enviado ou entregue: não dá para desfazer, a loja precisa agir
    AlreadyShipped,

    // O pedido continua pago por outro pagamento (ex.: o cliente pagou duas vezes e um foi estornado)
    StillPaid,
}
