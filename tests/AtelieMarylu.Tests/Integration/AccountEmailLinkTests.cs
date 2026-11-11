using System.Net;
using AtelieMarylu.Infrastructure.Data;
using AtelieMarylu.Web.Components.Account;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace AtelieMarylu.Tests.Integration;

// Os links que vão por e-mail (AccountEmailLinks): o token que sai no link
// precisa voltar intacto e funcionar uma vez só.
public class AccountEmailLinkTests(TestDatabase database) : IClassFixture<TestDatabase>
{
    private const string Password = "Original!123";

    [FactWithDatabase]
    public async Task Password_reset_link_works_once()
    {
        using var scope = database.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await CreateUserAsync(users);

        var link = await new AccountEmailLinks(users, new FakeNavigation()).PasswordResetAsync(user);
        var token = TokenFrom(link);

        Assert.StartsWith("https://loja.teste/Account/ResetPassword?", WebUtility.HtmlDecode(link));
        Assert.True((await users.ResetPasswordAsync(user, token, "Nova!Senha123")).Succeeded);
        Assert.True(await users.CheckPasswordAsync(user, "Nova!Senha123"));

        // A senha trocou o security stamp: o mesmo link não serve de novo
        Assert.False((await users.ResetPasswordAsync(user, token, "Outra!Senha123")).Succeeded);
    }

    [FactWithDatabase]
    public async Task Email_change_link_only_works_for_the_requested_email()
    {
        using var scope = database.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await CreateUserAsync(users);
        var newEmail = $"{Guid.NewGuid():N}@novo.local";

        var link = await new AccountEmailLinks(users, new FakeNavigation()).EmailChangeAsync(user, newEmail);
        var token = TokenFrom(link);

        // Trocar o ?email= do link por outro endereço não funciona
        Assert.False((await users.ChangeEmailAsync(user, "intruso@teste.local", token)).Succeeded);
        Assert.Equal(newEmail, Query(link)["email"]);
        Assert.True((await users.ChangeEmailAsync(user, newEmail, token)).Succeeded);
        Assert.Equal(newEmail, user.Email);
        Assert.True(user.EmailConfirmed);
    }

    private static Dictionary<string, Microsoft.Extensions.Primitives.StringValues> Query(string htmlEncodedLink) =>
        QueryHelpers.ParseQuery(new Uri(WebUtility.HtmlDecode(htmlEncodedLink)).Query);

    private static string TokenFrom(string htmlEncodedLink) => AccountEmailLinks.DecodeToken(Query(htmlEncodedLink)["code"]!)!;

    private static async Task<ApplicationUser> CreateUserAsync(UserManager<ApplicationUser> users)
    {
        var email = $"{Guid.NewGuid():N}@teste.local";
        var user = new ApplicationUser { UserName = email, Email = email, FullName = "Cliente Teste" };
        Assert.True((await users.CreateAsync(user, Password)).Succeeded);
        return user;
    }

    private sealed class FakeNavigation : NavigationManager
    {
        public FakeNavigation() => Initialize("https://loja.teste/", "https://loja.teste/");
    }
}

// Não precisa de banco: fica fora da classe acima para rodar mesmo sem ATELIE_TESTE_DB
public class AccountEmailLinkDecodeTests
{
    [Fact]
    public void Corrupted_code_is_rejected_without_exception()
    {
        Assert.Null(AccountEmailLinks.DecodeToken("%%%não-é-base64%%%"));
    }
}
