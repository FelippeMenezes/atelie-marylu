namespace AtelieMarylu.Web.Configuration;

// O banco guarda tudo em UTC. Para exibir, convertemos para o horário de Brasília,
// independente do fuso do servidor (em nuvem ele costuma ser UTC).
public static class DateTimeExtensions
{
    private static readonly TimeZoneInfo BrasiliaTimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    public static DateTime ToBrasiliaTime(this DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), BrasiliaTimeZone);

    public static string FormatBrasilia(this DateTime utc, string format = "dd/MM/yyyy HH:mm") => utc.ToBrasiliaTime().ToString(format);
}
