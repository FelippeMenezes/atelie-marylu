using AtelieMarylu.Domain.Email;
using AtelieMarylu.Domain.Security;
using AtelieMarylu.Infrastructure.Data;
using AtelieMarylu.Web;
using AtelieMarylu.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;

namespace AtelieMarylu.Tests.Integration;

// Cadastro de ponta a ponta (AccountRegistrationService), com o UserManager e o banco de verdade.
// Protege a ligação entre as peças: se alguém voltar a mostrar o erro do Identity ou esquecer o aviso
// ao dono, estes testes falham (os testes das peças separadas continuariam verdes).
public class AccountRegistrationTests(TestDatabase database) : IClassFixture<TestDatabase>, IDisposable
{
    private readonly List<ServiceProvider> providers = [];

    private const string Password = "Senha!Forte123";
    private static readonly RegistrationLinks Links = new("https://loja.teste/", "https://loja.teste/Account/ForgotPassword");

    [FactWithDatabase]
    public async Task New_email_creates_a_customer_and_sends_the_welcome()
    {
        using var scope = database.Services.CreateScope();
        var (service, client, users) = await BuildAsync(scope);
        var email = $"{Guid.NewGuid()}@teste.local";

        var result = await service.RegisterAsync(new ApplicationUser { FullName = "Maria" }, email, Password, Links);

        Assert.True(result.Succeeded);
        var saved = (await users.FindByEmailAsync(email))!;
        Assert.True(await users.IsInRoleAsync(saved, AppRoles.Customer));
        Assert.Equal(email, Assert.Single(client.Sent).To);
    }

    [FactWithDatabase]
    public async Task Existing_email_gets_a_neutral_message_and_the_owner_is_notified()
    {
        using var scope = database.Services.CreateScope();
        var (service, client, users) = await BuildAsync(scope);
        var owner = await CreateOwnerAsync(users);

        // Maiúsculas diferentes: o Identity compara pela versão normalizada
        var typed = owner.Email!.ToUpperInvariant();
        var result = await service.RegisterAsync(new ApplicationUser { FullName = "Intrusa" }, typed, Password, Links);

        Assert.False(result.Succeeded);
        var error = Assert.Single(result.Errors);
        Assert.Equal("RegistrationRefused", error.Code);
        // A mensagem não confirma que o e-mail é de um cliente
        Assert.DoesNotContain(owner.Email!, error.Description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(nameof(IdentityErrorDescriber.DuplicateEmail), error.Code);

        // O aviso vai para o e-mail gravado do dono, com o nome dele (nada do que a intrusa digitou)
        var notice = Assert.Single(client.Sent);
        Assert.Equal(owner.Email, notice.To);
        Assert.DoesNotContain("Intrusa", notice.Html);

        // Nenhuma conta nova foi criada
        await using var db = database.CreateDbContext();
        Assert.Equal(1, await db.Users.CountAsync(u => u.NormalizedEmail == typed));
    }

    [FactWithDatabase]
    public async Task Existing_email_with_weak_password_shows_only_the_password_error()
    {
        using var scope = database.Services.CreateScope();
        var (service, client, users) = await BuildAsync(scope);
        var owner = await CreateOwnerAsync(users);

        var result = await service.RegisterAsync(new ApplicationUser { FullName = "Maria" }, owner.Email!, "fraca", Links);

        Assert.False(result.Succeeded);
        Assert.All(result.Errors, error => Assert.StartsWith("Password", error.Code));
        // O aviso só sai quando o único problema é o e-mail: quem errou a senha ainda não chegou lá
        Assert.Empty(client.Sent);
    }

    public void Dispose()
    {
        foreach (var provider in providers) provider.Dispose();
        GC.SuppressFinalize(this);
    }

    // Pelo UserManager, e não direto no banco: o Identity procura o e-mail pela versão normalizada
    private static async Task<ApplicationUser> CreateOwnerAsync(UserManager<ApplicationUser> users)
    {
        var email = $"{Guid.NewGuid()}@teste.local";
        var owner = new ApplicationUser { UserName = email, Email = email, FullName = "Dona da Conta" };
        Assert.True((await users.CreateAsync(owner, Password)).Succeeded);
        return owner;
    }

    private async Task<(AccountRegistrationService Service, FakeEmailClient Client, UserManager<ApplicationUser> Users)> BuildAsync(IServiceScope scope)
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        // Mesma regra da loja (ServiceCollectionExtensions): um e-mail por conta
        users.Options.User.RequireUniqueEmail = true;
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roles.RoleExistsAsync(AppRoles.Customer)) await roles.CreateAsync(new IdentityRole(AppRoles.Customer));

        // Os e-mails são montados de verdade (Razor + traduções) e entregues a um cliente falso
        var client = new FakeEmailClient();
        var emailServices = new ServiceCollection();
        emailServices.AddLogging();
        emailServices.AddLocalization(options => options.ResourcesPath = "Resources");
        emailServices.AddMemoryCache();
        emailServices.AddSingleton<IEmailClient>(client);
        emailServices.AddTransient<AccountEmailSender>();
        var provider = emailServices.BuildServiceProvider();
        providers.Add(provider);

        var service = new AccountRegistrationService(users, provider.GetRequiredService<AccountEmailSender>(),
            provider.GetRequiredService<IStringLocalizer<SharedResource>>(), NullLogger<AccountRegistrationService>.Instance);
        return (service, client, users);
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
