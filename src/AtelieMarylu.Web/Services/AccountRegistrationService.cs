using AtelieMarylu.Domain.Security;
using AtelieMarylu.Infrastructure.Data;
using AtelieMarylu.Web.Components.Account;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Localization;

namespace AtelieMarylu.Web.Services;

// Cadastro de cliente com e-mail e senha (Register.razor). Fica num serviço, e não na tela, para que a regra
// de segurança abaixo seja testada de ponta a ponta contra o banco (AccountRegistrationTests).
//
// E-mail já cadastrado: a tela não confirma que ele é de um cliente (enumeração de contas). Devolve um erro
// neutro e o dono recebe o aviso por e-mail. Os outros erros (senha fraca) aparecem normalmente.
public sealed class AccountRegistrationService(
    UserManager<ApplicationUser> userManager,
    AccountEmailSender emails,
    IStringLocalizer<SharedResource> L,
    ILogger<AccountRegistrationService> logger)
{
    public async Task<IdentityResult> RegisterAsync(ApplicationUser user, string email, string password, RegistrationLinks links)
    {
        // O nome de usuário da loja é o próprio e-mail. O CreateAsync preenche as versões normalizadas.
        user.UserName = email;
        user.Email = email;
        var result = await userManager.CreateAsync(user, password);

        if (!result.Succeeded)
        {
            var (emailTaken, others) = RegistrationErrors.Split(result.Errors);

            // Outros erros aparecem primeiro e sem citar o e-mail. Quando a pessoa corrigir, o próximo
            // envio cai no caso abaixo, se o e-mail for mesmo de outra conta.
            if (others.Count > 0)
                return IdentityResult.Failed([.. others]);

            if (emailTaken)
                await NotifyOwnerAsync(email, links.ForgotPassword);

            return IdentityResult.Failed(new IdentityError
            {
                Code = "RegistrationRefused",
                Description = L["We could not create an account with this email. If you already have an account, sign in or use \"Forgot your password?\"."],
            });
        }

        logger.LogInformation("User {UserId} created a new account with password.", user.Id);
        await userManager.AddToRoleAsync(user, AppRoles.Customer);

        // Só boas-vindas, sem link de confirmação: a loja não exige confirmar o e-mail (AML-0027)
        await emails.TrySendWelcomeAsync(user, email, links.StoreHome);
        return result;
    }

    private async Task NotifyOwnerAsync(string typedEmail, string forgotPasswordUrl)
    {
        var owner = await userManager.FindByEmailAsync(typedEmail) ?? await userManager.FindByNameAsync(typedEmail);
        if (owner?.Email is not { } ownerEmail)
            return;

        // O log leva só o Id, sem o e-mail digitado (dado pessoal e texto livre vindo do formulário)
        logger.LogInformation("Registration attempted with the email of existing user {UserId}.", owner.Id);
        await emails.TrySendExistingAccountNoticeAsync(owner, ownerEmail, forgotPasswordUrl);
    }
}

// Endereços que vão nos e-mails do cadastro. Vêm da tela (AccountEmailLinks depende da requisição).
public sealed record RegistrationLinks(string StoreHome, string ForgotPassword);
