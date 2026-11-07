using System.Globalization;

namespace AtelieMarylu.Domain.Payments;

// A loja vende em reais em qualquer idioma. O formato "C" comum usaria a moeda do idioma
// escolhido: em inglês, R$ 39,90 viraria $39.90, um preço errado.
public static class MoneyExtensions
{
    private static readonly CultureInfo Brazil = CultureInfo.GetCultureInfo("pt-BR");

    public static string ToBrl(this decimal amount) => amount.ToString("C", Brazil);
}
