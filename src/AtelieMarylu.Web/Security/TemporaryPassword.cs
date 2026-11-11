using System.Security.Cryptography;

namespace AtelieMarylu.Web.Security;

// Senha temporária criada quando o Admin redefine a senha de alguém pelo painel.
// Usa RandomNumberGenerator (criptográfico), nunca Random: a sequência do Random pode ser prevista.
// Deixa de fora os caracteres que se confundem ao ditar ou copiar (0/O, 1/l/I).
public static class TemporaryPassword
{
    public const int Length = 12;

    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnpqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Symbols = "!@#$%*?";

    public static string Generate()
    {
        // Um caractere de cada tipo atende às regras padrão do Identity (maiúscula, minúscula, número e símbolo)
        var chars = new List<char> { Pick(Upper), Pick(Lower), Pick(Digits), Pick(Symbols) };
        const string all = Upper + Lower + Digits + Symbols;
        while (chars.Count < Length) chars.Add(Pick(all));

        // Embaralha para que o tipo de cada posição não seja previsível
        var result = chars.ToArray();
        RandomNumberGenerator.Shuffle(result.AsSpan());
        return new string(result);
    }

    private static char Pick(string from) => from[RandomNumberGenerator.GetInt32(from.Length)];
}
