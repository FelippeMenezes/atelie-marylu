using System.Net;
using System.Text.RegularExpressions;
using AtelieMarylu.Domain.Email;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AtelieMarylu.Infrastructure.Integrations;

// Enviador de desenvolvimento: em vez de mandar o e-mail, escreve no log o destinatário, o assunto
// e os links. Assim dá para testar "esqueci minha senha" e a confirmação copiando o link do terminal,
// sem gastar a cota do Resend nem mandar e-mail de teste para gente real.
// NUNCA em produção: quem lê o log poderia usar o link. Por isso ele se recusa a funcionar fora
// de Development (falha fechado), mesmo que alguém o registre por engano.
public partial class LoggingEmailClient(ILogger<LoggingEmailClient> logger, IHostEnvironment environment) : IEmailClient
{
    [GeneratedRegex("href=\"([^\"]+)\"")]
    private static partial Regex Link();

    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (!environment.IsDevelopment())
            throw new InvalidOperationException("LoggingEmailClient só pode ser usado em Development: configure Resend:ApiKey e Resend:From.");

        // O link vai INTEIRO para o log: é o motivo de esta classe existir (copiar do terminal e testar
        // o fluxo). Isso é aceitável só porque ela se recusa a rodar fora de Development (acima) e a loja
        // nem sobe fora de Development sem o Resend (ResendOptions). O CodeQL aponta "exposure of private
        // information" aqui; o alerta foi dispensado no GitHub com esta justificativa.
        // O destinatário não é necessário para o teste, então vai mascarado.
        // O HTML traz os links codificados (&amp; no lugar de &): o log mostra a versão para colar no navegador.
        var links = Link()
            .Matches(message.Html)
            .Select(m => WebUtility.HtmlDecode(m.Groups[1].Value))
            .Distinct();

        logger.LogInformation(
            "[E-mail simulado] Para {To} | {Subject} | {Links}",
            MaskEmail(message.To),
            message.Subject,
            string.Join(" ", links));

        return Task.CompletedTask;
    }

    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 0 || at == email.Length - 1)
            return "***";

        var local = email[..at];
        var domain = email[(at + 1)..];

        var maskedLocal = local.Length <= 2 ? "***" : $"{local[0]}***{local[^1]}";
        var dot = domain.LastIndexOf('.');
        var maskedDomain = dot > 1
            ? $"{domain[0]}***{domain[(dot - 1)..]}"
            : "***";

        return $"{maskedLocal}@{maskedDomain}";
    }
}
