namespace AtelieMarylu.Infrastructure.Integrations;

public class TurnstileOptions
{
    public const string SectionName = "Turnstile";

    public string SiteKey { get; set; } = "";
    public string SecretKey { get; set; } = "";

    public bool IsActive => !string.IsNullOrWhiteSpace(SiteKey) && !string.IsNullOrWhiteSpace(SecretKey);
}
