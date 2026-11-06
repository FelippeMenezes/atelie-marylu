namespace AtelieMarylu.Domain.Pagamentos;

// Uma única conta de desconto PIX para a vitrine, o checkout e o pedido:
// assim o valor mostrado ao cliente é sempre igual ao valor cobrado.
public static class CalculadoraDescontoPix
{
    public static decimal Calcular(decimal valor, decimal percentual) =>
        Math.Round(valor * percentual / 100m, 2, MidpointRounding.AwayFromZero);
}
