using AtelieMarylu.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;

namespace AtelieMarylu.Web.Services;

// Recuperação de conta feita pelo próprio cliente (link do "esqueci minha senha").
// Precisa ter o mesmo efeito do reset pelo painel (AccessService.ResetPasswordAsync), menos a troca obrigatória:
// aqui a pessoa já escolheu a senha nova, então não faz sentido pedir outra no próximo login.
public sealed class AccountRecoveryService(UserManager<ApplicationUser> userManager, ILogger<AccountRecoveryService> logger)
{
    public async Task<IdentityResult> ResetPasswordAsync(ApplicationUser user, string token, string newPassword)
    {
        // Confere o token ANTES de mexer no usuário: com um link falso ou vencido, ninguém consegue
        // desbloquear a conta nem tirar a troca obrigatória de senha.
        var validToken = await userManager.VerifyUserTokenAsync(user,
            userManager.Options.Tokens.PasswordResetTokenProvider, UserManager<ApplicationUser>.ResetPasswordTokenPurpose, token);
        if (!validToken)
            return IdentityResult.Failed(userManager.ErrorDescriber.InvalidToken());

        // Tudo marcado antes do reset: o ResetPasswordAsync salva o usuário inteiro, então estas mudanças
        // são gravadas junto com a senha nova, num único UPDATE (ou nada é gravado, se a senha for recusada).
        // - Quem esquece a senha costuma estar bloqueado pelas 5 tentativas erradas.
        // - Se o Admin tinha redefinido a senha antes, a troca obrigatória já foi cumprida com a senha nova.
        user.LockoutEnd = null;
        user.AccessFailedCount = 0;
        user.MustChangePassword = false;
        // Abrir o link que chegou na caixa de entrada prova que o e-mail é da pessoa: vale como confirmação.
        // É isso que permite mandar o link também para contas não confirmadas (veja ForgotPassword.razor).
        user.EmailConfirmed = true;

        // Valida a senha nova, troca o hash e renova o security stamp (o link deixa de valer
        // e os outros aparelhos são desconectados em até 1 minuto)
        var result = await userManager.ResetPasswordAsync(user, token, newPassword);
        if (result.Succeeded)
            logger.LogInformation("User {UserId} reset their password by email link.", user.Id);
        return result;
    }
}
