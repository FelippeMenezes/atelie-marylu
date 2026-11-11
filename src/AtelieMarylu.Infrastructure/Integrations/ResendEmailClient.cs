using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AtelieMarylu.Domain.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AtelieMarylu.Infrastructure.Integrations;

// Entrega o e-mail pela API HTTP do Resend (POST /emails). Só transporta: o texto e o idioma
// são montados no projeto Web (AccountEmailSender). A API dispensa pacote de SMTP e segue o
// mesmo padrão do Turnstile: HttpClient + Options.
public class ResendEmailClient(HttpClient http, IOptions<ResendOptions> options, ILogger<ResendEmailClient> logger) : IEmailClient
{
    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var settings = options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, "emails")
        {
            Content = JsonContent.Create(new ResendEmail(
                settings.From,
                [message.To],
                message.Subject,
                message.Html,
                string.IsNullOrWhiteSpace(settings.ReplyTo) ? null : settings.ReplyTo)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            // O corpo da resposta explica o erro (ex.: domínio não verificado). O conteúdo do e-mail
            // (que tem o link com token) e a chave nunca vão para o log.
            var error = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("Resend recusou o envio do e-mail: {Status} {Error}", (int)response.StatusCode, error);
            response.EnsureSuccessStatusCode();
        }
    }

    private sealed record ResendEmail(
        string From,
        string[] To,
        string Subject,
        string Html,
        [property: JsonPropertyName("reply_to"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ReplyTo);
}
