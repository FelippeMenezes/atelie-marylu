using AtelieMarylu.Domain.Auditing;
using AtelieMarylu.Domain.Security;
using AtelieMarylu.Infrastructure.Data;
using AtelieMarylu.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AtelieMarylu.Tests.Integration;

// Redefinição de senha pelo painel (AccessService.ResetPasswordAsync)
public class PasswordResetTests(TestDatabase database) : IClassFixture<TestDatabase>
{
    private const string OriginalPassword = "Original!123";
    private const string AdminId = "admin-que-redefine";

    [FactWithDatabase]
    public async Task Reset_sets_temporary_password_forces_change_unlocks_and_audits()
    {
        using var scope = database.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await CreateUserAsync(users);
        await users.AccessFailedAsync(user);
        await users.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddMinutes(15));
        var oldStamp = user.SecurityStamp;

        var temporary = await Service(scope).ResetPasswordAsync(user.Id, AdminId);

        var saved = (await users.FindByIdAsync(user.Id))!;
        Assert.True(await users.CheckPasswordAsync(saved, temporary));
        Assert.False(await users.CheckPasswordAsync(saved, OriginalPassword));
        Assert.True(saved.MustChangePassword);
        Assert.NotEqual(oldStamp, saved.SecurityStamp);
        Assert.False(await users.IsLockedOutAsync(saved));
        Assert.Equal(0, saved.AccessFailedCount);

        await using var db = database.CreateDbContext();
        var entry = await db.AuditEntries.SingleAsync(a => a.Action == AuditActions.PasswordReset && a.EntityId == user.Email);
        Assert.DoesNotContain(temporary, entry.Details ?? "");
    }

    [FactWithDatabase]
    public async Task Admin_cannot_reset_own_password()
    {
        using var scope = database.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await CreateUserAsync(users);

        var ex = await Assert.ThrowsAsync<AccessException>(() => Service(scope).ResetPasswordAsync(user.Id, user.Id));
        Assert.Equal("You cannot reset your own password here.", ex.Message);
    }

    [FactWithDatabase]
    public async Task Admin_password_cannot_be_reset()
    {
        using var scope = database.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roles.RoleExistsAsync(AppRoles.Admin)) await roles.CreateAsync(new IdentityRole(AppRoles.Admin));
        var otherAdmin = await CreateUserAsync(users);
        await users.AddToRoleAsync(otherAdmin, AppRoles.Admin);

        var ex = await Assert.ThrowsAsync<AccessException>(() => Service(scope).ResetPasswordAsync(otherAdmin.Id, AdminId));
        Assert.Equal("Admin passwords cannot be reset here.", ex.Message);
        Assert.True(await users.CheckPasswordAsync((await users.FindByIdAsync(otherAdmin.Id))!, OriginalPassword));
    }

    [FactWithDatabase]
    public async Task Google_only_account_is_refused()
    {
        using var scope = database.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await CreateUserAsync(users, withPassword: false);

        var ex = await Assert.ThrowsAsync<AccessException>(() => Service(scope).ResetPasswordAsync(user.Id, AdminId));
        Assert.Equal("This account signs in with Google and has no password to reset.", ex.Message);
        Assert.False(await users.HasPasswordAsync((await users.FindByIdAsync(user.Id))!));
    }

    private AccessService Service(IServiceScope scope) => new(
        scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(),
        scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>(),
        database,
        new AuditService(new CurrentUserService(new ServiceCollection().BuildServiceProvider(), new HttpContextAccessor()), database));

    private static async Task<ApplicationUser> CreateUserAsync(UserManager<ApplicationUser> users, bool withPassword = true)
    {
        var email = $"{Guid.NewGuid():N}@teste.local";
        var user = new ApplicationUser { UserName = email, Email = email, FullName = "Cliente Teste" };
        var result = withPassword ? await users.CreateAsync(user, OriginalPassword) : await users.CreateAsync(user);
        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(e => e.Description)));
        return user;
    }
}
