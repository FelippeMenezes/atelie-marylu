namespace AtelieMarylu.Web.Localization;

public static class SupportedLanguages
{
    public const string Default = "pt-BR";

    // Código e nome de cada idioma, no próprio idioma: quem não lê português também encontra o seu
    public static readonly IReadOnlyList<(string Code, string Name)> All =
    [
        ("pt-BR", "Português"),
        ("es", "Español"),
        ("en", "English"),
    ];

    public static string[] Codes => All.Select(l => l.Code).ToArray();

    public static bool IsSupported(string? code) => All.Any(l => l.Code == code);
}
