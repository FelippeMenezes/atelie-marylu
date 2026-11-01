using Microsoft.Extensions.Options;

namespace AtelieMarylu.Web.Integracoes;

public class TurnstileOptions
{
    public const string Secao = "Turnstile";

    public string SiteKey { get; set; } = "";
    public string SecretKey { get; set; } = "";

    public bool Ativo => !string.IsNullOrWhiteSpace(SiteKey) && !string.IsNullOrWhiteSpace(SecretKey);
}

// Verificação anti-robô do Cloudflare Turnstile (gratuita e sem "clique nos semáforos").
// Protege login e cadastro contra tentativas automatizadas. Desligado se não houver chaves.
public class Turnstile(HttpClient http, IOptions<TurnstileOptions> opcoes, ILogger<Turnstile> logger)
{
    public const string CampoFormulario = "cf-turnstile-response";

    public bool Ativo => opcoes.Value.Ativo;
    public string SiteKey => opcoes.Value.SiteKey;

    public async Task<bool> ValidarAsync(string? token, string? ip, CancellationToken ct = default)
    {
        if (!Ativo) return true;
        if (string.IsNullOrWhiteSpace(token)) return false;

        try
        {
            var dados = new Dictionary<string, string> { ["secret"] = opcoes.Value.SecretKey, ["response"] = token };
            if (ip is not null) dados["remoteip"] = ip;
            using var conteudo = new FormUrlEncodedContent(dados);
            using var resposta = await http.PostAsync("turnstile/v0/siteverify", conteudo, ct);
            var resultado = await resposta.Content.ReadFromJsonAsync<Resultado>(ct);
            return resultado?.Success == true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Se o Cloudflare cair, preferimos bloquear (falhar fechado) a abrir a porta para robôs
            logger.LogError(ex, "Falha ao validar Turnstile");
            return false;
        }
    }

    private sealed record Resultado(bool Success);
}
