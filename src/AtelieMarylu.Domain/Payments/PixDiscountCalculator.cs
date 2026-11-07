namespace AtelieMarylu.Domain.Payments;

// Uma única conta de desconto PIX para a vitrine, o checkout e o pedido.
// O arredondamento vale para o valor recebido: a vitrine calcula por unidade e o pedido sobre o
// subtotal, então com várias unidades o total pode diferir em centavos do preço unitário × quantidade.
public static class PixDiscountCalculator
{
    public static decimal Calculate(decimal amount, decimal percent) =>
        Math.Round(amount * percent / 100m, 2, MidpointRounding.AwayFromZero);
}
