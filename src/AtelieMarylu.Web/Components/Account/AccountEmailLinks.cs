using System.Text;
using System.Text.Encodings.Web;
using AtelieMarylu.Infrastructure.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;

namespace AtelieMarylu.Web.Components.Account;

// Monta os links que vão por e-mail (confirmação, redefinição de senha, troca de e-mail).
// No modelo do .NET, esse código se repetia em cada tela. Aqui fica num lugar só.
//
// Segurança: o endereço do link vem da requisição (NavigationManager). Sem AllowedHosts,
// um atacante poderia trocar o cabeçalho Host e receber o token num site dele ("password reset poisoning").
// É por isso que a loja não sobe em produção com AllowedHosts=*.
public sealed class AccountEmailLinks(UserManager<ApplicationUser> userManager, NavigationManager navigation)
{
    public async Task<string> EmailConfirmationAsync(ApplicationUser user)
    {
        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        return Build("Account/ConfirmEmail", new()
        {
            ["userId"] = await userManager.GetUserIdAsync(user),
            ["code"] = Encode(token),
        });
    }

    public async Task<string> PasswordResetAsync(ApplicationUser user)
    {
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        return Build("Account/ResetPassword", new() { ["code"] = Encode(token) });
    }

    public async Task<string> EmailChangeAsync(ApplicationUser user, string newEmail)
    {
        var token = await userManager.GenerateChangeEmailTokenAsync(user, newEmail);
        return Build("Account/ConfirmEmailChange", new()
        {
            ["userId"] = await userManager.GetUserIdAsync(user),
            ["email"] = newEmail,
            ["code"] = Encode(token),
        });
    }

    // Desfaz o Encode na tela que recebe o link. Devolve null se o código vier corrompido.
    public static string? DecodeToken(string code)
    {
        try
        {
            return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    // O token tem +, / e =, que quebram a URL. O Base64Url troca por caracteres seguros.
    private static string Encode(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    // O contrato do IEmailSender<TUser> é receber o link já codificado para HTML
    private string Build(string page, Dictionary<string, object?> query) =>
        HtmlEncoder.Default.Encode(navigation.GetUriWithQueryParameters(navigation.ToAbsoluteUri(page).AbsoluteUri, query));
}
