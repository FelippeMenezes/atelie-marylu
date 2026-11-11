using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AtelieMarylu.Domain.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AtelieMarylu.Infrastructure.Integrations;

// Entrega o e-mail pela API HTTP do Resend (POST /emails). Só transporta: o texto e o idioma
// são montados no projeto Web (AccountEmailSender). A API dispensa pacote de SMTP e segue o
// mesmo padrão do Turnstile: HttpClient + Options. Qualquer falha de entrega vira EmailDeliveryException.
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

        try
        {
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                // O corpo da resposta explica o erro (ex.: domínio não verificado). O conteúdo do e-mail
                // (que tem o link com token) e a chave nunca vão para o log.
                var error = await response.Content.ReadAsStringAsync(ct);
                logger.LogError("Resend recusou o envio do e-mail: {Status} {Error}", (int)response.StatusCode, error);
                throw new EmailDeliveryException($"O Resend recusou o e-mail ({(int)response.StatusCode}).");
            }
        }
        catch (HttpRequestException ex)
        {
            // Sem conexão, DNS, TLS: o Resend não foi alcançado
            throw new EmailDeliveryException("Não foi possível falar com o Resend.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // Passou do Timeout do HttpClient (10 s). Se quem chamou cancelou, a exceção segue como está.
            throw new EmailDeliveryException("O Resend demorou demais para responder.", ex);
        }
    }

    private sealed record ResendEmail(
        string From,
        string[] To,
        string Subject,
        string Html,
        [property: JsonPropertyName("reply_to"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ReplyTo);
}
