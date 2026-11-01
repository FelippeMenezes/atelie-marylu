namespace AtelieMarylu.Web.Configuracao;

// O banco guarda tudo em UTC. Para exibir, convertemos para o horário de Brasília,
// independente do fuso do servidor (em nuvem ele costuma ser UTC).
public static class Horario
{
    private static readonly TimeZoneInfo Brasilia = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    public static DateTime ParaBrasilia(this DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Brasilia);

    public static string Formatar(this DateTime utc, string formato = "dd/MM/yyyy HH:mm") => utc.ParaBrasilia().ToString(formato);
}
