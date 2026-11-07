using System.Globalization;
using System.Text;

namespace AtelieMarylu.Domain.Catalog;

public static class SlugGenerator
{
    // "Bolsa de Tecido Floral" -> "bolsa-de-tecido-floral" (para URLs amigáveis)
    public static string Create(string text)
    {
        var withoutAccents = new StringBuilder();
        foreach (var c in text.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark))
        {
            withoutAccents.Append(c);
        }

        var slug = new StringBuilder();
        var lastWasHyphen = true;
        foreach (var c in withoutAccents.ToString().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                slug.Append(c);
                lastWasHyphen = false;
            }
            else if (!lastWasHyphen)
            {
                slug.Append('-');
                lastWasHyphen = true;
            }
        }
        return slug.ToString().TrimEnd('-');
    }
}
