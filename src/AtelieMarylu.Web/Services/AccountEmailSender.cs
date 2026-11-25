using System.Globalization;
using System.Net;
using AtelieMarylu.Domain.Email;
using AtelieMarylu.Infrastructure.Data;
using AtelieMarylu.Web.Components.Emails;
using AtelieMarylu.Web.Localization;
using AtelieMarylu.Web.Security;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Localization;

namespace AtelieMarylu.Web.Services;

// Monta os e-mails da conta (boas-vindas, redefinição de senha, troca de e-mail, aviso de cadastro repetido) e entrega ao IEmailClient (Resend ou log).
// É o IEmailSender<ApplicationUser> que as telas do Identity usam.
//
// - O HTML vem de componentes Razor (Components/Emails), renderizados fora de uma tela pelo HtmlRenderer.
//   O Razor codifica tudo o que escreve, então o nome do cliente não consegue injetar HTML no e-mail,
//   e os textos são chaves L["..."] conferidas pelo LocalizationTests como os das telas.
// - O idioma é o da requisição (cookie de cultura): o e-mail sai no idioma em que a pessoa usava o site.
public sealed class AccountEmailSender(
    IEmailClient client,
    IStringLocalizer<SharedResource> L,
    IServiceProvider services,
    IMemoryCache cache,
    ILoggerFactory loggerFactory,
    ILogger<AccountEmailSender> logger) : IEmailSender<ApplicationUser>
{
    // Na loja, só a troca de e-mail usa este método: o link vai para o e-mail novo (Manage/Email.razor)
    public async Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink)
    {
        var html = await RenderAsync<ConfirmEmailMessage>(new()
        {
            [nameof(ConfirmEmailMessage.Name)] = user.FullName,
            [nameof(ConfirmEmailMessage.Link)] = Decode(confirmationLink),
            [nameof(ConfirmEmailMessage.Hours)] = (int)AccountTokenLifespans.EmailConfirmation.TotalHours,
        });
        await client.SendAsync(new EmailMessage(email, L["Confirm email · Ateliê Marylu"], html));
    }

    public async Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink)
    {
        var html = await RenderAsync<ResetPasswordMessage>(new()
        {
            [nameof(ResetPasswordMessage.Name)] = user.FullName,
            [nameof(ResetPasswordMessage.Link)] = Decode(resetLink),
            [nameof(ResetPasswordMessage.Hours)] = (int)AccountTokenLifespans.PasswordReset.TotalHours,
        });
        await client.SendAsync(new EmailMessage(email, L["Create a new password · Ateliê Marylu"], html));
    }

    // Boas-vindas no cadastro. Não faz parte do IEmailSender<ApplicationUser> do Identity, por isso
    // as telas de cadastro recebem o AccountEmailSender diretamente.
    public async Task SendWelcomeAsync(ApplicationUser user, string email, string storeUrl)
    {
        var html = await RenderAsync<WelcomeMessage>(new()
        {
            [nameof(WelcomeMessage.Name)] = user.FullName,
            [nameof(WelcomeMessage.StoreUrl)] = storeUrl,
        });
        await client.SendAsync(new EmailMessage(email, L["Welcome to Ateliê Marylu"], html));
    }

    // Usado logo depois de criar a conta (cadastro com senha e com Google). A conta já existe: uma falha
    // no e-mail não pode impedir a pessoa de seguir com a compra, então ela fica só no log.
    public async Task TrySendWelcomeAsync(ApplicationUser user, string email, string storeUrl)
    {
        try
        {
            await SendWelcomeAsync(user, email, storeUrl);
        }
        catch (EmailDeliveryException ex)
        {
            logger.LogError(ex, "Could not send the welcome email to user {UserId}.", user.Id);
        }
    }

    // Prazo mínimo entre dois avisos de "tentaram criar conta com seu e-mail" para a mesma conta
    public static readonly TimeSpan ExistingAccountNoticeInterval = TimeSpan.FromHours(1);

    // Cadastro com um e-mail que já tem conta: a tela mostra uma mensagem neutra e o dono recebe este aviso.
    //
    // - Limite de um aviso por conta a cada hora: sem ele, alguém poderia repetir o cadastro com o e-mail
    //   de outra pessoa para lotar a caixa dela (e gastar a cota do Resend). O Turnstile e o limite por IP
    //   já freiam robôs; este limite protege a vítima mesmo de tentativas vindas de vários IPs.
    // - O controle fica na memória (IMemoryCache), como o OrderEvents: vale para um único servidor e zera
    //   quando a aplicação reinicia, o que no pior caso permite um aviso a mais.
    // - A chave é o Id do usuário, não o e-mail, para não guardar dado pessoal no cache.
    // - Sai no idioma padrão da loja, não no da requisição: quem tentou o cadastro não é o dono da conta.
    //   Sem isso, quem navega em espanhol faria a dona receber um aviso de segurança que ela não lê.
    //   (A loja não guarda o idioma de cada cliente; se passar a guardar, use o dele aqui.)
    // - Falha no envio fica só no log: a tela não pode se comportar diferente por causa dela.
    public async Task TrySendExistingAccountNoticeAsync(ApplicationUser user, string email, string forgotPasswordUrl)
    {
        var key = $"existing-account-notice:{user.Id}";
        if (cache.TryGetValue(key, out _))
        {
            logger.LogInformation("Existing account notice for user {UserId} skipped (already sent recently).", user.Id);
            return;
        }
        // Marca antes de enviar: se o Resend estiver fora do ar, as novas tentativas também não insistem
        cache.Set(key, true, ExistingAccountNoticeInterval);

        var previousCulture = CultureInfo.CurrentCulture;
        var previousUICulture = CultureInfo.CurrentUICulture;
        var storeCulture = CultureInfo.GetCultureInfo(SupportedLanguages.Default);
        CultureInfo.CurrentCulture = storeCulture;
        CultureInfo.CurrentUICulture = storeCulture;
        try
        {
            var html = await RenderAsync<ExistingAccountMessage>(new()
            {
                [nameof(ExistingAccountMessage.Name)] = user.FullName,
                [nameof(ExistingAccountMessage.ForgotPasswordUrl)] = forgotPasswordUrl,
            });
            await client.SendAsync(new EmailMessage(email, L["Someone tried to create an account with your email"], html));
        }
        catch (EmailDeliveryException ex)
        {
            logger.LogError(ex, "Could not send the existing account notice to user {UserId}.", user.Id);
        }
        finally
        {
            // Devolve o idioma da requisição: o resto da tela continua no idioma de quem está navegando
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUICulture;
        }
    }

    // Só é usado pela API de Identity (MapIdentityApi), que a loja não usa
    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode) =>
        throw new NotSupportedException("A loja redefine a senha por link, não por código.");

    // O contrato do IEmailSender<TUser> entrega o link já codificado para HTML. O componente Razor
    // codifica de novo ao escrever, então aqui se desfaz a primeira codificação (senão & viraria &amp;amp;).
    private static string Decode(string htmlEncodedLink) => WebUtility.HtmlDecode(htmlEncodedLink);

    private Task<string> RenderAsync<TComponent>(Dictionary<string, object?> parameters) where TComponent : IComponent =>
        EmailHtml.RenderAsync<TComponent>(services, loggerFactory, parameters);
}
