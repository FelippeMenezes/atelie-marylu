namespace AtelieMarylu.Web.Services;

// Monta o padrão do ILike ("contém o texto"). No ILike, % e _ são curingas: o escape faz
// com que sejam procurados como texto normal quando a pessoa os digita na busca.
public static class PadraoBusca
{
    public const string CaractereDeEscape = "\\";

    public static string Contem(string texto)
    {
        var escapado = texto.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        return $"%{escapado}%";
    }
}
