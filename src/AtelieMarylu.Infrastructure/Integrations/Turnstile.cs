using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AtelieMarylu.Infrastructure.Integrations;

// Verificação anti-robô do Cloudflare Turnstile (gratuita e sem "clique nos semáforos").
// Protege login e cadastro contra tentativas automatizadas. Desligado se não houver chaves.
public class Turnstile(HttpClient http, IOptions<TurnstileOptions> options, ILogger<Turnstile> logger)
{
    public const string FormField = "cf-turnstile-response";

    public bool IsActive => options.Value.IsActive;
    public string SiteKey => options.Value.SiteKey;

    public async Task<bool> ValidateAsync(string? token, string? ip, CancellationToken ct = default)
    {
        if (!IsActive) return true;
        if (string.IsNullOrWhiteSpace(token)) return false;

        try
        {
            var data = new Dictionary<string, string> { ["secret"] = options.Value.SecretKey, ["response"] = token };
            if (ip is not null) data["remoteip"] = ip;
            using var content = new FormUrlEncodedContent(data);
            using var response = await http.PostAsync("turnstile/v0/siteverify", content, ct);
            var result = await response.Content.ReadFromJsonAsync<TurnstileResult>(ct);
            return result?.Success == true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Se o Cloudflare cair, preferimos bloquear (falhar fechado) a abrir a porta para robôs
            logger.LogError(ex, "Falha ao validar Turnstile");
            return false;
        }
    }

    private sealed record TurnstileResult(bool Success);
}
