using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace AtelieMarylu.Domain;

public static class Texto
{
    // "Bolsa de Tecido Floral" -> "bolsa-de-tecido-floral" (para URLs amigáveis)
    public static string Slug(string texto)
    {
        var semAcento = new StringBuilder();
        foreach (var c in texto.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark))
        {
            semAcento.Append(c);
        }

        var slug = new StringBuilder();
        var ultimoFoiHifen = true;
        foreach (var c in semAcento.ToString().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                slug.Append(c);
                ultimoFoiHifen = false;
            }
            else if (!ultimoFoiHifen)
            {
                slug.Append('-');
                ultimoFoiHifen = true;
            }
        }
        return slug.ToString().TrimEnd('-');
    }

    // Sem 0/O e 1/I/L para o cliente não confundir ao ditar o código
    private const string AlfabetoCodigo = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

    // Código público do pedido (ex.: AML-7K3F9Q). Não revela quantos pedidos a loja já teve.
    public static string NovoCodigoPedido()
    {
        Span<char> codigo = stackalloc char[6];
        for (var i = 0; i < codigo.Length; i++)
            codigo[i] = AlfabetoCodigo[RandomNumberGenerator.GetInt32(AlfabetoCodigo.Length)];
        return $"AML-{codigo}";
    }
}
