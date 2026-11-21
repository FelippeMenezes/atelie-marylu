namespace AtelieMarylu.Domain.Orders;

// O frete subiu entre a cotação que a cliente viu e a confirmação do pedido. O pedido NÃO é criado:
// a cliente precisa ver o valor novo e confirmar de novo (preço mostrado = preço cobrado, como pede o CDC).
// Tipo próprio para o checkout saber que deve recalcular o frete, e não só mostrar a mensagem.
public sealed class ShippingPriceChangedException(string newAmount)
    : OrderRuleException("The shipping price changed to {0}. Check the new amount and confirm again.", newAmount);
