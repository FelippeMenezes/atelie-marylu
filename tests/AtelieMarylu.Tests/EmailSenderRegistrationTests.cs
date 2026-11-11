using AtelieMarylu.Domain.Email;
using AtelieMarylu.Infrastructure;
using AtelieMarylu.Infrastructure.Integrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AtelieMarylu.Tests;

// Garante que o link com token nunca vai para o log de produção:
// fora de Development, ou o e-mail sai pelo Resend, ou a loja não sobe.
public class EmailSenderRegistrationTests
{
    [Fact]
    public void Production_without_resend_fails_on_startup()
    {
        using var services = Build(Environments.Production, withResend: false);

        // É isto que o ValidateOnStart executa quando a aplicação sobe
        var ex = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<ResendOptions>>().Value);
        Assert.Contains("Resend:ApiKey", ex.Message);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public void With_resend_configured_emails_go_through_resend(string environment)
    {
        using var services = Build(environment, withResend: true);

        Assert.IsType<ResendEmailClient>(services.GetRequiredService<IEmailClient>());
    }

    [Fact]
    public void Development_without_resend_logs_the_link()
    {
        using var services = Build(Environments.Development, withResend: false);

        Assert.IsType<LoggingEmailClient>(services.GetRequiredService<IEmailClient>());
    }

    // Segunda trava: mesmo registrado por engano, o LoggingEmailClient não escreve o link fora de Development
    [Fact]
    public async Task Logging_client_refuses_outside_development()
    {
        var client = new LoggingEmailClient(NullLogger<LoggingEmailClient>.Instance, new FakeEnvironment(Environments.Production));

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync(
            new EmailMessage("cliente@teste.local", "Assunto", "<a href=\"https://loja.teste/Account/ResetPassword?code=segredo\">x</a>")));
    }

    private static ServiceProvider Build(string environment, bool withResend)
    {
        var settings = new Dictionary<string, string?>
        {
            // Só para o registro do DbContext; nenhum teste aqui conecta no banco
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=nao-usado",
        };
        if (withResend)
        {
            settings["Resend:ApiKey"] = "re_teste";
            settings["Resend:From"] = "Loja <loja@teste.local>";
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new FakeEnvironment(environment));
        services.AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), new FakeEnvironment(environment));
        return services.BuildServiceProvider();
    }

    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "AtelieMarylu.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
