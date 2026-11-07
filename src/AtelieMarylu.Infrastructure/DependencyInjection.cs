using AtelieMarylu.Domain.Shipping;
using AtelieMarylu.Domain.Payments;
using AtelieMarylu.Infrastructure.Data;
using AtelieMarylu.Infrastructure.Integrations;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AtelieMarylu.Infrastructure;

// Registra tudo o que este projeto oferece. O Program.cs só precisa chamar AddInfrastructure.
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
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

        return services;
    }
}
