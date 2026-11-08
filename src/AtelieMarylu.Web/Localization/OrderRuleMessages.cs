using System.Globalization;
using AtelieMarylu.Domain.Orders;
using Microsoft.Extensions.Localization;

namespace AtelieMarylu.Web.Localization;

// Monta a mensagem de um OrderRuleException já traduzida.
public static class OrderRuleMessages
{
    // Os argumentos que são status do pedido também são traduzidos.
    // Sem isso, a frase sairia misturada: "Pedido está Paid e não pode receber pagamento."
    public static string Translate(this IStringLocalizer localizer, OrderRuleException ex) =>
        localizer[ex.Template, [.. ex.Arguments.Select(argument => argument is OrderStatus status
            ? localizer[Order.Describe(status)].Value
            : argument)]];

    // Mesma mensagem, sempre no idioma padrão da loja (pt-BR). Usada no que fica gravado no banco,
    // como a auditoria: o registro não pode depender do idioma de quem estava usando o site na hora.
    public static string TranslateToStoreLanguage(this IStringLocalizer localizer, OrderRuleException ex)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(SupportedLanguages.Default);
            return localizer.Translate(ex);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
