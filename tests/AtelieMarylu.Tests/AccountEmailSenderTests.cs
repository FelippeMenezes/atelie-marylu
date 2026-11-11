using System.Globalization;
using AtelieMarylu.Domain.Email;
using AtelieMarylu.Infrastructure.Data;
using AtelieMarylu.Web;
using AtelieMarylu.Web.Security;
using AtelieMarylu.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace AtelieMarylu.Tests;

// Os e-mails da conta: idioma, nome codificado (sem injeção de HTML) e link intacto
public class AccountEmailSenderTests
{
    // O Identity entrega o link já codificado para HTML (&amp;), como faz o AccountEmailLinks
    private const string EncodedLink = "https://loja.teste/Account/ResetPassword?code=abc&amp;x=1";

    [Fact]
    public async Task Reset_email_is_in_the_request_language()
    {
        var (sent, html) = await SendResetAsync("pt-BR", "Maria");

        Assert.Equal("Criar nova senha · Ateliê Marylu", sent.Subject);
        // O Razor escreve acentos como entidades (&#xE1;): os programas de e-mail mostram normalmente
        Assert.Contains("Olá, Maria!", Text(html));
        Assert.Contains("lang=\"pt-BR\"", html);
        Assert.Contains($"{(int)AccountTokenLifespans.PasswordReset.TotalHours} horas", html);

        var (sentEs, htmlEs) = await SendResetAsync("es", "Maria");
        Assert.Equal("Crear nueva contraseña · Ateliê Marylu", sentEs.Subject);
        Assert.Contains("¡Hola, Maria!", Text(htmlEs));
    }

    [Fact]
    public async Task Name_cannot_inject_html()
    {
        var (_, html) = await SendResetAsync("pt-BR", "<a href=\"https://golpe.teste\">Clique</a>");

        Assert.DoesNotContain("<a href=\"https://golpe.teste\"", html);
        Assert.Contains("&lt;a href=", html);
    }

    [Fact]
    public async Task Link_is_encoded_exactly_once()
    {
        var (_, html) = await SendResetAsync("pt-BR", "Maria");

        Assert.Contains("href=\"https://loja.teste/Account/ResetPassword?code=abc&amp;x=1\"", html);
        Assert.DoesNotContain("&amp;amp;", html);
    }

    [Fact]
    public async Task Email_change_email_uses_its_own_lifespan()
    {
        using var services = BuildServices(out var client);
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo("pt-BR");
        try
        {
            await services.GetRequiredService<AccountEmailSender>().SendConfirmationLinkAsync(
                new ApplicationUser { FullName = "" }, "cliente@teste.local", EncodedLink);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }

        var sent = Assert.Single(client.Sent);
        Assert.Equal("Confirmar e-mail · Ateliê Marylu", sent.Subject);
        Assert.Contains("Olá!", Text(sent.Html)); // sem nome, cumprimento genérico
        Assert.Contains($"{(int)AccountTokenLifespans.EmailConfirmation.TotalHours} horas", sent.Html);
    }

    [Fact]
    public async Task Welcome_email_has_no_token_and_explains_what_to_do()
    {
        using var services = BuildServices(out var client);
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo("pt-BR");
        try
        {
            await services.GetRequiredService<AccountEmailSender>().SendWelcomeAsync(
                new ApplicationUser { FullName = "Maria" }, "cliente@teste.local", "https://loja.teste/");
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }

        var sent = Assert.Single(client.Sent);
        Assert.Equal("Boas-vindas ao Ateliê Marylu", sent.Subject);
        Assert.Contains("Olá, Maria!", Text(sent.Html));
        Assert.Contains("href=\"https://loja.teste/\"", sent.Html);
        // Boas-vindas não leva link com token (nem de confirmação, nem de senha)
        Assert.DoesNotContain("code=", sent.Html);
        Assert.Contains("Esqueceu sua senha?", Text(sent.Html));
    }

    [Fact]
    public async Task Welcome_failure_does_not_reach_the_registration()
    {
        // A conta já foi criada quando o e-mail sai: uma falha no envio não pode derrubar o cadastro
        using var services = BuildServices(out _, new FailingEmailClient());

        await services.GetRequiredService<AccountEmailSender>().TrySendWelcomeAsync(
            new ApplicationUser { FullName = "Maria" }, "cliente@teste.local", "https://loja.teste/");
    }

    [Fact]
    public void Confirmation_and_reset_tokens_are_protected_separately()
    {
        // Nomes diferentes = chaves diferentes: um token de confirmação não serve para redefinir a senha
        var confirmation = new EmailConfirmationTokenProviderOptions();
        Assert.NotEqual(new Microsoft.AspNetCore.Identity.DataProtectionTokenProviderOptions().Name, confirmation.Name);
        Assert.True(AccountTokenLifespans.PasswordReset < AccountTokenLifespans.EmailConfirmation);
    }

    private static string Text(string html) => System.Net.WebUtility.HtmlDecode(html);

    private static async Task<(EmailMessage Sent, string Html)> SendResetAsync(string culture, string name)
    {
        using var services = BuildServices(out var client);
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        try
        {
            await services.GetRequiredService<AccountEmailSender>().SendPasswordResetLinkAsync(
                new ApplicationUser { FullName = name }, "cliente@teste.local", EncodedLink);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
        var sent = Assert.Single(client.Sent);
        return (sent, sent.Html);
    }

    private static ServiceProvider BuildServices(out FakeEmailClient client, IEmailClient? replacement = null)
    {
        client = new FakeEmailClient();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization(options => options.ResourcesPath = "Resources");
        services.AddSingleton(replacement ?? client);
        services.AddTransient<AccountEmailSender>();
        return services.BuildServiceProvider();
    }

    private sealed class FailingEmailClient : IEmailClient
    {
        public Task SendAsync(EmailMessage message, CancellationToken ct = default) =>
            throw new EmailDeliveryException("Resend fora do ar (simulado)");
    }

    private sealed class FakeEmailClient : IEmailClient
    {
        public List<EmailMessage> Sent { get; } = [];

        public Task SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }
}
