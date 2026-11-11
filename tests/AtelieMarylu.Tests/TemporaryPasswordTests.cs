using AtelieMarylu.Infrastructure.Data;
using AtelieMarylu.Web.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace AtelieMarylu.Tests;

public class TemporaryPasswordTests
{
    [Fact]
    public async Task Generated_password_passes_the_identity_rules()
    {
        // Mesmas regras do AddIdentityCore: padrão do Identity com mínimo de 8 caracteres
        var options = new IdentityOptions();
        options.Password.RequiredLength = 8;
        using var userManager = new UserManager<ApplicationUser>(new NoStore(), Options.Create(options),
            null!, [], [], null!, new IdentityErrorDescriber(), null!, null!);
        var validator = new PasswordValidator<ApplicationUser>();

        // Gera várias para pegar o caso raro em que o sorteio deixaria um tipo de caractere de fora
        for (var i = 0; i < 200; i++)
        {
            var password = TemporaryPassword.Generate();
            var result = await validator.ValidateAsync(userManager, new ApplicationUser(), password);
            Assert.True(result.Succeeded, $"'{password}': {string.Join(", ", result.Errors.Select(e => e.Code))}");
            Assert.Equal(TemporaryPassword.Length, password.Length);
        }
    }

    [Fact]
    public void Generated_passwords_are_not_repeated()
    {
        var passwords = Enumerable.Range(0, 1000).Select(_ => TemporaryPassword.Generate()).ToHashSet();
        Assert.Equal(1000, passwords.Count);
    }

    [Fact]
    public void Generated_password_has_no_ambiguous_characters()
    {
        var password = string.Concat(Enumerable.Range(0, 200).Select(_ => TemporaryPassword.Generate()));
        Assert.DoesNotContain(password, c => "0O1lI".Contains(c));
    }

    // O validador de senha só usa as opções do UserManager; o store nunca é chamado
    private sealed class NoStore : IUserStore<ApplicationUser>
    {
        public void Dispose() { }
        public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken ct) => throw new NotSupportedException();
        public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken ct) => throw new NotSupportedException();
        public Task SetUserNameAsync(ApplicationUser user, string? userName, CancellationToken ct) => throw new NotSupportedException();
        public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken ct) => throw new NotSupportedException();
        public Task SetNormalizedUserNameAsync(ApplicationUser user, string? normalizedName, CancellationToken ct) => throw new NotSupportedException();
        public Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken ct) => throw new NotSupportedException();
        public Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken ct) => throw new NotSupportedException();
        public Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken ct) => throw new NotSupportedException();
        public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken ct) => throw new NotSupportedException();
        public Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken ct) => throw new NotSupportedException();
    }
}
