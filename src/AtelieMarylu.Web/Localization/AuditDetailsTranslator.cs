using System.Text.RegularExpressions;
using AtelieMarylu.Domain.Payments;
using Microsoft.Extensions.Localization;

namespace AtelieMarylu.Web.Localization;

// A auditoria guarda o detalhe como texto, gravado uma vez em inglês (ex.: "CAN-6AYBHB · Paid",
// "Approved via pagbank (ORDE_123)"). Quem lê o histórico pode estar em outro idioma, por isso a tradução
// acontece na hora de mostrar, e não ao gravar: o mesmo registro aparece em pt-BR, es ou en.
//
// O texto é separado pelos " · " e só os pedaços conhecidos mudam: chaves do .resx (ex.: situação do pedido),
// "<situação do pagamento> via <provedor>" e a forma de pagamento. Códigos, valores, motivos escritos por
// pessoas e registros antigos em português continuam como foram gravados.
public static partial class AuditDetailsTranslator
{
    private const string Separator = " · ";

    public static string? Translate(IStringLocalizer localizer, string? details)
    {
        if (string.IsNullOrEmpty(details)) return details;
        return string.Join(Separator, details.Split(Separator).Select(part => TranslatePart(localizer, part)));
    }

    private static string TranslatePart(IStringLocalizer localizer, string part)
    {
        var known = localizer[part];
        if (!known.ResourceNotFound) return known.Value;

        if (IsName<PaymentMethod>(part, out var method))
            return method == PaymentMethod.Pix ? "PIX" : localizer["Card"].Value;

        // Aviso do provedor: "Approved via pagbank (ORDE_123)"
        var match = PaymentNotice().Match(part);
        if (match.Success && IsName<PaymentStatus>(match.Groups["status"].Value, out var status))
            return $"{localizer[Payment.Describe(status)].Value} via {match.Groups["rest"].Value}";

        return part;
    }

    // Só aceita o nome exato do enum: o Enum.TryParse também aceitaria números ("5") e trocaria um valor por um nome
    private static bool IsName<TEnum>(string text, out TEnum value) where TEnum : struct, Enum
    {
        value = default;
        return Enum.GetNames<TEnum>().Contains(text) && Enum.TryParse(text, out value);
    }

    [GeneratedRegex(@"^(?<status>[A-Za-z]+) via (?<rest>.+)$")]
    private static partial Regex PaymentNotice();
}
