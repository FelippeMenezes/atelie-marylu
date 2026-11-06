namespace AtelieMarylu.Infrastructure.Integrations;

public class TurnstileOptions
{
    public const string Secao = "Turnstile";

    public string SiteKey { get; set; } = "";
    public string SecretKey { get; set; } = "";

    public bool Ativo => !string.IsNullOrWhiteSpace(SiteKey) && !string.IsNullOrWhiteSpace(SecretKey);
}
