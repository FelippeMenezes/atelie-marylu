using System.Globalization;
using System.Text;
using System.Xml;

namespace AtelieMarylu.Web.Seo;

// Limpeza de texto digitado no painel antes de ir para formatos rígidos (XML do feed).
// Texto colado do Word, de PDF ou do WhatsApp pode trazer caracteres invisíveis que o XML proíbe,
// e um corte no lugar errado pode partir um emoji ao meio. Nos dois casos o XmlWriter lança exceção,
// e um único produto derrubaria o feed do catálogo inteiro.
public static class SafeText
{
    // Remove o que o XML 1.0 não aceita: caracteres de controle (exceto tab, quebra de linha e retorno)
    // e metades soltas de pares substitutos (meio emoji).
    public static string ForXml(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c))
            {
                // Par completo (emoji etc.): mantém as duas metades juntas
                if (i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    sb.Append(c).Append(text[i + 1]);
                    i++;
                }
                // Metade sem par: descarta
                continue;
            }
            if (XmlConvert.IsXmlChar(c))
                sb.Append(c);
        }
        return sb.ToString();
    }

    // Corta em até "max" posições sem partir um caractere visível. Usa "elementos de texto" (o que a pessoa
    // enxerga como um caractere), então não separa emoji, emoji com tom de pele nem letra com acento combinado.
    public static string Truncate(string text, int max, string ellipsis = "...")
    {
        if (text.Length <= max) return text;

        var limit = max - ellipsis.Length;
        var end = 0;
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            var next = enumerator.ElementIndex + ((string)enumerator.Current).Length;
            if (next > limit) break;
            end = next;
        }
        return text[..end] + ellipsis;
    }
}
