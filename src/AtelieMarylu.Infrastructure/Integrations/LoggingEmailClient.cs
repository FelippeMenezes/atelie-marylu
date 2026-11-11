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

        // O HTML traz os links codificados (&amp; no lugar de &). Para evitar expor dados privados no log,
        // registramos somente versão redigida dos links e e-mail mascarado.
        var links = Link()
            .Matches(message.Html)
            .Select(m => WebUtility.HtmlDecode(m.Groups[1].Value))
            .Select(RedactLink)
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

    private static string RedactLink(string link)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri))
            return "[invalid-link]";

        return $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}";
    }
}
