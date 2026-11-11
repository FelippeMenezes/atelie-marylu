using System.Net;
using AtelieMarylu.Domain.Email;
using AtelieMarylu.Infrastructure.Data;
using AtelieMarylu.Web.Components.Emails;
using AtelieMarylu.Web.Security;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Localization;

namespace AtelieMarylu.Web.Services;

// Monta os e-mails da conta (boas-vindas, redefinição de senha, troca de e-mail) e entrega ao IEmailClient (Resend ou log).
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

    // Só é usado pela API de Identity (MapIdentityApi), que a loja não usa
    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode) =>
        throw new NotSupportedException("A loja redefine a senha por link, não por código.");

    // O contrato do IEmailSender<TUser> entrega o link já codificado para HTML. O componente Razor
    // codifica de novo ao escrever, então aqui se desfaz a primeira codificação (senão & viraria &amp;amp;).
    private static string Decode(string htmlEncodedLink) => WebUtility.HtmlDecode(htmlEncodedLink);

    private async Task<string> RenderAsync<TComponent>(Dictionary<string, object?> parameters) where TComponent : IComponent
    {
        await using var renderer = new HtmlRenderer(services, loggerFactory);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(parameters));
            return output.ToHtmlString();
        });
    }
}
