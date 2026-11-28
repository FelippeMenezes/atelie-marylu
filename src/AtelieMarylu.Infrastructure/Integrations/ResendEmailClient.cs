using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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
        // Mesmo e-mail reenviado com a mesma chave em até 24 h: o Resend devolve a resposta original e não manda
        // de novo. Com a mesma chave e conteúdo diferente, ele responde 409.
        if (!string.IsNullOrEmpty(message.IdempotencyKey))
            request.Headers.Add("Idempotency-Key", message.IdempotencyKey);

        try
        {
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var name = await ReadErrorNameAsync(response, ct);
                if (name == InvalidIdempotentRequest && !string.IsNullOrEmpty(message.IdempotencyKey))
                {
                    // Mesma chave, conteúdo diferente (ex.: o deploy derrubou o worker depois do envio e trouxe um
                    // template novo). O Resend não guarda requisições que falharam: se a chave existe, o envio anterior
                    // DEU CERTO. Tratar como entregue evita o e-mail duplicado depois que a chave expira (24 h).
                    logger.LogWarning("Resend: a chave de idempotência já tinha sido usada com outro conteúdo; o e-mail já havia sido aceito.");
                    return;
                }

                // Só o status e o código do erro (ex.: "validation_error"): a mensagem do provedor pode repetir o
                // destinatário, que é dado pessoal. O conteúdo do e-mail e a chave da API nunca vão para o log.
                logger.LogError("Resend recusou o envio do e-mail: {Status} {ErrorName}", (int)response.StatusCode, name ?? "-");
                throw new EmailDeliveryException($"O Resend recusou o e-mail ({(int)response.StatusCode} {name}).",
                    isTransient: IsTransient(response.StatusCode));
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

    private const string InvalidIdempotentRequest = "invalid_idempotent_request";

    // Classifica pela pergunta "de quem é o problema?":
    // - Do PRÓPRIO e-mail (dados inválidos 400/422, conteúdo grande demais 413): repetir dá o mesmo erro. Definitivo.
    // - De todo o resto, passageiro: o Resend (5xx), o volume (429, inclusive a cota do dia), a mesma chave em
    //   andamento (409 concurrent_idempotent_requests), a demora (408) e a CONFIGURAÇÃO da loja (401 chave vencida,
    //   403 domínio sem verificação, 404 endereço errado). Configuração errada não é culpa do e-mail: alguém da
    //   equipe corrige, e os e-mails que esperavam saem sozinhos, em vez de serem todos abandonados.
    public static bool IsTransient(HttpStatusCode status) =>
        status is not (HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity or HttpStatusCode.RequestEntityTooLarge);

    // O corpo de erro do Resend é {"statusCode":422,"name":"validation_error","message":"..."}. Devolve só o "name",
    // e só se for um código simples (letras e "_"): ele vai para o log e para o banco (OutboxEmail.LastError).
    private static async Task<string?> ReadErrorNameAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return json.RootElement.ValueKind == JsonValueKind.Object
                && json.RootElement.TryGetProperty("name", out var name)
                && name.GetString() is { Length: > 0 and <= 64 } text
                && text.All(c => c is (>= 'a' and <= 'z') or '_')
                ? text : null;
        }
        catch (JsonException)
        {
            return null; // ex.: página HTML de um proxy no caminho
        }
    }

    private sealed record ResendEmail(
        string From,
        string[] To,
        string Subject,
        string Html,
        [property: JsonPropertyName("reply_to"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ReplyTo);
}
