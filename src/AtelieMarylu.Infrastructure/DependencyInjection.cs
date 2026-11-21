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
using Microsoft.Extensions.Logging;

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

        // Integrações externas: trocar a implementação não exige mudar nenhuma tela.
        AddShipping(services, configuration, environment);

        // Pagamento: com token e endereço público do PagBank, cobra de verdade pelo Checkout PagBank
        // (em desenvolvimento, no sandbox). Sem eles, o simulado, cujo webhook só existe em Development.
        var pagBankSection = configuration.GetSection(PagBankOptions.SectionName);
        services.AddOptions<PagBankOptions>()
            .Bind(pagBankSection)
            // Um pagamento do sandbox não move dinheiro. Em produção, ele marcaria um pedido real como pago.
            .Validate(o => !o.IsActive || !o.Sandbox || environment.IsDevelopment(),
                "PagBank:Sandbox precisa ser false fora de Development (no servidor: PAGBANK_SANDBOX=false).")
            .Validate(o => !o.IsActive || (Uri.TryCreate(o.PublicBaseUrl, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps),
                "PagBank:PublicBaseUrl precisa ser um endereço https completo (ex.: https://www.ateliemarylu.com.br).")
            .ValidateOnStart();

        var pagBank = pagBankSection.Get<PagBankOptions>();
        if (pagBank?.IsActive == true)
        {
            services.AddSingleton<PagBankPublicKeyCache>();
            // Registro das chamadas para a homologação (desligado por padrão: PagBank:LogTraffic)
            services.AddSingleton<PagBankTrafficRecorder>();
            services.AddTransient<PagBankTrafficHandler>();
            services.AddHttpClient<IPaymentGateway, PagBankPaymentGateway>(http =>
            {
                http.BaseAddress = pagBank.ApiBaseUrl;
                http.Timeout = TimeSpan.FromSeconds(10);
            }).AddHttpMessageHandler<PagBankTrafficHandler>();
        }
        else
        {
            services.AddSingleton<IPaymentGateway, SimulatedPaymentGateway>();
        }

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

    // Frete: a retirada no ateliê sempre existe; as transportadoras vêm do Melhor Envio quando há token.
    //   - Com token (qualquer ambiente): retirada + Melhor Envio.
    //   - Sem token, em Development: o simulado (retirada + PAC/SEDEX inventados), para testar sem conta.
    //   - Sem token, fora de Development: só a retirada. Nunca um frete inventado cobrado de uma cliente real.
    private static void AddShipping(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var section = configuration.GetSection(MelhorEnvioOptions.SectionName);
        services.AddOptions<MelhorEnvioOptions>()
            .Bind(section)
            // Cotação do sandbox tem preço de mentira: em produção, a cliente pagaria um frete que não existe
            .Validate(o => !o.IsActive || !o.Sandbox || environment.IsDevelopment(),
                "MelhorEnvio:Sandbox precisa ser false fora de Development (no servidor: MELHORENVIO_SANDBOX=false).")
            .Validate(o => !o.IsActive || o.NormalizedOriginPostalCode is not null,
                "MelhorEnvio:OriginPostalCode precisa ser o CEP do ateliê, com 8 dígitos (no servidor: MELHORENVIO_ORIGIN_POSTAL_CODE).")
            .Validate(o => !o.IsActive || o.ContactEmail.Contains('@'),
                "MelhorEnvio:ContactEmail precisa ser o e-mail de contato da loja: o Melhor Envio exige (no servidor: MELHORENVIO_CONTACT_EMAIL).")
            .Validate(o => o.HasValidServices,
                "MelhorEnvio:Services precisa ser uma lista de números separados por vírgula (ex.: 1,2,17) ou ficar vazio.")
            .ValidateOnStart();

        var melhorEnvio = section.Get<MelhorEnvioOptions>();
        if (melhorEnvio?.IsActive == true)
        {
            services.AddSingleton<MelhorEnvioQuoteCache>();
            services.AddHttpClient<MelhorEnvioShippingCalculator>(http =>
            {
                http.BaseAddress = melhorEnvio.ApiBaseUrl;
                // A cliente está esperando na tela do checkout: melhor desistir cedo
                http.Timeout = TimeSpan.FromSeconds(8);
                // A resposta normal tem poucos KB: um teto protege a memória se a API responder algo absurdo
                http.MaxResponseContentBufferSize = 1024 * 1024;
            });
            services.AddTransient<IShippingCalculator>(sp => new ShippingQuoteAggregator(
                [new StudioPickupCalculator(), sp.GetRequiredService<MelhorEnvioShippingCalculator>()],
                sp.GetRequiredService<ILogger<ShippingQuoteAggregator>>()));
        }
        else if (environment.IsDevelopment())
        {
            services.AddSingleton<IShippingCalculator, SimulatedShippingCalculator>();
        }
        else
        {
            services.AddSingleton<IShippingCalculator, StudioPickupCalculator>();
        }
    }
}
