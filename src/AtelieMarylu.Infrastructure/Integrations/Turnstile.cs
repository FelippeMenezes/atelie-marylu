using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AtelieMarylu.Infrastructure.Integrations;

// Verificação anti-robô do Cloudflare Turnstile (gratuita e sem "clique nos semáforos").
// Protege login e cadastro contra tentativas automatizadas. Desligado se não houver chaves.
public class Turnstile(HttpClient http, IOptions<TurnstileOptions> options, ILogger<Turnstile> logger)
{
    public const string FormField = "cf-turnstile-response";

    // Limite documentado pelo Cloudflare: um token maior é recusado sem chamar a API
    public const int MaxTokenLength = 2048;

    public bool IsActive => options.Value.IsActive;
    public string SiteKey => options.Value.SiteKey;

    public async Task<bool> ValidateAsync(string? token, string? ip, CancellationToken ct = default)
    {
        if (!IsActive) return true;
        if (string.IsNullOrWhiteSpace(token) || token.Length > MaxTokenLength) return false;

        try
        {
            var data = new Dictionary<string, string> { ["secret"] = options.Value.SecretKey, ["response"] = token };
            if (ip is not null) data["remoteip"] = ip;
            using var content = new FormUrlEncodedContent(data);
            using var response = await http.PostAsync("turnstile/v0/siteverify", content, ct);
            var result = await response.Content.ReadFromJsonAsync<TurnstileResult>(ct);
            if (result?.Success == true) return true;

            // Os códigos (ex.: "timeout-or-duplicate") ajudam a diagnosticar. O token e a chave nunca vão para o log.
            logger.LogWarning("Turnstile recusou a verificação: {ErrorCodes}", string.Join(", ", result?.ErrorCodes ?? []));
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // Se o Cloudflare cair ou responder algo que não é JSON, preferimos bloquear
            // (falhar fechado) a abrir a porta para robôs
            logger.LogError(ex, "Falha ao validar Turnstile");
            return false;
        }
    }

    private sealed record TurnstileResult(
        bool Success,
        [property: JsonPropertyName("error-codes")] string[]? ErrorCodes);
}
