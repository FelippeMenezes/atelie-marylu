using AtelieMarylu.Domain.Shipping;
using AtelieMarylu.Domain.Payments;
using AtelieMarylu.Domain.Email;
using AtelieMarylu.Infrastructure.Data;
using AtelieMarylu.Infrastructure.Integrations;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AtelieMarylu.Infrastructure;

// Registra tudo o que este projeto oferece. O Program.cs só precisa chamar AddInfrastructure.
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

        // A factory permite criar um DbContext curto por operação, o recomendado no Blazor Server
        // (um mesmo circuito pode disparar operações em paralelo). Também registra o DbContext
        // como scoped, que é o que o Identity usa.
        services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString), ServiceLifetime.Scoped);

        services.AddDataProtection()
            .SetApplicationName("AtelieMarylu")
            .PersistKeysToDbContext<ApplicationDbContext>();

        services.Configure<TurnstileOptions>(configuration.GetSection(TurnstileOptions.SectionName));

        // Integrações externas: trocar estas duas linhas pelas implementações reais
        // (Melhor Envio e Mercado Pago) não exige mudar nenhuma tela.
        services.AddSingleton<IShippingCalculator, SimulatedShippingCalculator>();
        services.AddSingleton<IPaymentGateway, SimulatedPaymentGateway>();

        services.AddHttpClient<PostalCodeLookup>(http =>
        {
            http.BaseAddress = new Uri("https://viacep.com.br/");
            http.Timeout = TimeSpan.FromSeconds(5);
        });
        services.AddHttpClient<Turnstile>(http =>
        {
            http.BaseAddress = new Uri("https://challenges.cloudflare.com/");
            http.Timeout = TimeSpan.FromSeconds(10);
        });

        // Envio de e-mail. O texto (e o idioma) é montado no projeto Web; aqui só se escolhe quem entrega.
        var resendSection = configuration.GetSection(ResendOptions.SectionName);

        // Validada quando a aplicação sobe (como ShopOptions): fora de Development, sem chave e remetente
        // do Resend a loja NÃO sobe. Falha cedo e com mensagem clara, em vez de o "esqueci minha senha"
        // quebrar só quando um cliente usar, ou de os links irem parar no log.
        services.AddOptions<ResendOptions>()
            .Bind(resendSection)
            .Validate(o => environment.IsDevelopment() || o.IsActive,
                "Configure Resend:ApiKey e Resend:From (no servidor: RESEND_API_KEY e RESEND_FROM no .env). " +
                "Fora de Development a loja precisa enviar e-mail de verdade.")
            .ValidateOnStart();

        // Com chave configurada, envia de verdade (em qualquer ambiente: é assim que se testa o envio real
        // em desenvolvimento). Sem chave, só sobra Development, e aí o link vai para o log.
        // O LoggingEmailClient ainda tem a própria trava: fora de Development ele lança exceção.
        if (resendSection.Get<ResendOptions>()?.IsActive == true)
        {
            services.AddHttpClient<IEmailClient, ResendEmailClient>(http =>
            {
                http.BaseAddress = new Uri("https://api.resend.com/");
                http.Timeout = TimeSpan.FromSeconds(10);
            });
        }
        else
        {
            services.AddSingleton<IEmailClient, LoggingEmailClient>();
        }

        return services;
    }
}
