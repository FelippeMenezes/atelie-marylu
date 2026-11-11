using AtelieMarylu.Infrastructure.Data;
using AtelieMarylu.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AtelieMarylu.Tests.Integration;

// "Esqueci minha senha" pelo link (AccountRecoveryService): mesmo efeito do reset pelo painel,
// sem a troca obrigatória, e nada muda com um link inválido.
public class AccountRecoveryTests(TestDatabase database) : IClassFixture<TestDatabase>
{
    private const string OldPassword = "Original!123";
    private const string NewPassword = "Nova!Senha123";

    [FactWithDatabase]
    public async Task Reset_by_link_unlocks_and_removes_mandatory_change()
    {
        using var scope = database.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await CreateLockedUserWithMandatoryChangeAsync(users);
        var token = await users.GeneratePasswordResetTokenAsync(user);

        var result = await Service(users).ResetPasswordAsync(user, token, NewPassword);

        Assert.True(result.Succeeded);
        var saved = (await users.FindByIdAsync(user.Id))!;
        Assert.True(await users.CheckPasswordAsync(saved, NewPassword));
        Assert.False(await users.IsLockedOutAsync(saved));
        Assert.Equal(0, saved.AccessFailedCount);
        Assert.False(saved.MustChangePassword);
        // Quem abriu o link provou que o e-mail é dele
        Assert.True(saved.EmailConfirmed);
    }

    [FactWithDatabase]
    public async Task Invalid_link_changes_nothing()
    {
        using var scope = database.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await CreateLockedUserWithMandatoryChangeAsync(users);

        var result = await Service(users).ResetPasswordAsync(user, "token-falso", NewPassword);

        Assert.False(result.Succeeded);
        Assert.Equal(nameof(IdentityErrorDescriber.InvalidToken), Assert.Single(result.Errors).Code);
        // Simula outra parte do código salvando este mesmo usuário depois, no mesmo escopo:
        // se o serviço tivesse desbloqueado o objeto em memória antes de conferir o token, isso gravaria o desbloqueio
        await users.UpdateAsync(user);
        // Lê de um escopo novo: confere o que está no banco, não o objeto em memória
        using var other = database.Services.CreateScope();
        var saved = (await other.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(user.Id))!;
        Assert.True(saved.LockoutEnd > DateTimeOffset.UtcNow);
        Assert.True(saved.MustChangePassword);
        Assert.False(saved.EmailConfirmed);
        Assert.Equal(user.PasswordHash, saved.PasswordHash);
    }

    [FactWithDatabase]
    public async Task Weak_password_changes_nothing()
    {
        using var scope = database.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await CreateLockedUserWithMandatoryChangeAsync(users);
        var token = await users.GeneratePasswordResetTokenAsync(user);

        var result = await Service(users).ResetPasswordAsync(user, token, "curta");

        Assert.False(result.Succeeded);
        using var other = database.Services.CreateScope();
        var saved = (await other.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(user.Id))!;
        Assert.True(saved.LockoutEnd > DateTimeOffset.UtcNow);
        Assert.True(saved.MustChangePassword);
    }

    private static AccountRecoveryService Service(UserManager<ApplicationUser> users) =>
        new(users, NullLogger<AccountRecoveryService>.Instance);

    // Situação típica: o Admin redefiniu a senha (troca obrigatória) e depois a pessoa errou a senha 5 vezes
    private static async Task<ApplicationUser> CreateLockedUserWithMandatoryChangeAsync(UserManager<ApplicationUser> users)
    {
        var email = $"{Guid.NewGuid():N}@teste.local";
        var user = new ApplicationUser { UserName = email, Email = email, FullName = "Cliente Teste", MustChangePassword = true };
        Assert.True((await users.CreateAsync(user, OldPassword)).Succeeded);
        await users.SetLockoutEnabledAsync(user, true);
        await users.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddMinutes(15));
        return user;
    }
}
