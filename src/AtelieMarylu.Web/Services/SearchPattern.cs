namespace AtelieMarylu.Web.Services;

// Monta o padrão do ILike ("contém o texto"). No ILike, % e _ são curingas: o escape faz
// com que sejam procurados como texto normal quando a pessoa os digita na busca.
public static class SearchPattern
{
    public const string EscapeCharacter = "\\";

    public static string Contains(string text)
    {
        var escaped = text.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        return $"%{escaped}%";
    }
}
