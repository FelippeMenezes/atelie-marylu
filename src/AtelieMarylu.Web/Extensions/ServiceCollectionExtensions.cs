using System.Threading.RateLimiting;
using AtelieMarylu.Domain.Seguranca;
using AtelieMarylu.Infrastructure.Data;
using AtelieMarylu.Web.Components.Account;
using AtelieMarylu.Web.Configuracao;
using AtelieMarylu.Web.Seguranca;
using AtelieMarylu.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Serilog;
using Serilog.Formatting.Compact;

namespace AtelieMarylu.Web.Extensions;

// Cada método registra um assunto. Assim o Program.cs vira um índice fácil de ler.
public static class ServiceCollectionExtensions
{
    // Logs estruturados: legíveis no desenvolvimento, JSON em produção (fácil de pesquisar e alertar)
    public static WebApplicationBuilder AddStructuredLogging(this WebApplicationBuilder builder)
    {
        builder.Services.AddSerilog((services, log) =>
        {
            log.ReadFrom.Configuration(builder.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext();
            if (builder.Environment.IsDevelopment()) log.WriteTo.Console();
            else log.WriteTo.Console(new RenderedCompactJsonFormatter());
        });
        return builder;
    }

    public static IServiceCollection AddWebInterface(this IServiceCollection services)
    {
        services.AddRazorComponents()
            .AddInteractiveServerComponents();

        services.AddCascadingAuthenticationState();
        services.AddScoped<IdentityRedirectManager>();
        services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();
        services.AddHttpContextAccessor();
        services.AddDatabaseDeveloperPageExceptionFilter();
        services.AddHealthChecks().AddDbContextCheck<ApplicationDbContext>("banco");

        return services;
    }

    public static IServiceCollection AddStoreAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var autenticacao = services.AddAuthentication(options =>
        {
            options.DefaultScheme = IdentityConstants.ApplicationScheme;
            options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
        });
        autenticacao.AddIdentityCookies();

        // Entrar com Google: só é ligado se tiver as chaves na configuração
        var secaoGoogle = configuration.GetSection(LoginGoogleOptions.Secao);
        services.Configure<LoginGoogleOptions>(secaoGoogle);
        var loginGoogle = secaoGoogle.Get<LoginGoogleOptions>() ?? new();
        if (loginGoogle.Ativo)
        {
            autenticacao.AddGoogle(options =>
            {
                options.ClientId = loginGoogle.ClientId;
                options.ClientSecret = loginGoogle.ClientSecret;
                // Usado para recusar e-mails que o Google não confirmou (veja LoginGoogle.razor)
                options.ClaimActions.MapJsonKey("email_verified", "email_verified");
            });
        }

        services.AddIdentityCore<ApplicationUser>(options =>
            {
                // Sem serviço de e-mail por enquanto; quando houver, voltar para true
                options.SignIn.RequireConfirmedAccount = false;
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 8;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                // Version2: sem a tabela de passkeys (login sem senha), que a loja não usa
                options.Stores.SchemaVersion = IdentitySchemaVersions.Version2;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        // Substitui a fábrica padrão para incluir o claim de troca de senha obrigatória
        services.AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>, FabricaClaimsUsuario>();

        // Reavalia o login a cada minuto: ao remover o acesso de um funcionário, ele perde o painel em até 1 min
        services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.FromMinutes(1));

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.HttpOnly = true;                           // JavaScript não lê o cookie de login
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;  // só trafega por HTTPS
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
        });

        services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

        return services;
    }

    // Cada permissão vira uma policy: passa quem é Admin ou tem o claim "permissao" correspondente
    // (direto no usuário ou herdado de um grupo).
    public static IServiceCollection AddStoreAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            foreach (var permissao in Permissoes.Todas.Keys)
            {
                options.AddPolicy(permissao, policy => policy.RequireAssertion(ctx =>
                    ctx.User.IsInRole(Papeis.Admin) || ctx.User.HasClaim(Permissoes.TipoClaim, permissao)));
            }
            options.AddPolicy(Permissoes.AcessarPainel, policy => policy.RequireAssertion(ctx =>
                ctx.User.IsInRole(Papeis.Admin) || ctx.User.HasClaim(c => c.Type == Permissoes.TipoClaim)));
            options.AddPolicy(Papeis.Admin, policy => policy.RequireRole(Papeis.Admin));
        });

        return services;
    }

    // Limita tentativas por IP nas telas de conta (login, cadastro, senha) e no webhook
    public static IServiceCollection AddStoreRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var caminho = context.Request.Path;
                var sensivel = HttpMethods.IsPost(context.Request.Method)
                    && (caminho.StartsWithSegments("/Account") || caminho.StartsWithSegments("/api/webhooks"));
                if (!sensivel)
                    return RateLimitPartition.GetNoLimiter("livre");

                var ip = context.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";
                return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(1),
                });
            });
        });

        return services;
    }

    // Em produção o site roda atrás de um proxy (HTTPS termina nele). Só confiamos no cabeçalho
    // X-Forwarded-For vindo das redes listadas em Proxy:RedesConfiaveis (ex.: "10.0.0.0/8");
    // sem isso, todo mundo pareceria ter o IP do proxy e o limite de tentativas valeria para a loja inteira.
    public static IServiceCollection AddReverseProxySupport(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            foreach (var rede in configuration.GetSection("Proxy:RedesConfiaveis").Get<string[]>() ?? [])
                options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(rede));
        });

        return services;
    }

    public static IServiceCollection AddStoreServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<LojaOptions>().Bind(configuration.GetSection(LojaOptions.Secao)).ValidateDataAnnotations().ValidateOnStart();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<PedidoEventos>();
        services.AddScoped<UsuarioAtual>();
        services.AddScoped<AuditoriaService>();
        services.AddScoped<CarrinhoService>();
        services.AddScoped<PedidoService>();
        services.AddScoped<ProdutoService>();
        services.AddScoped<ClienteService>();
        services.AddScoped<PainelService>();
        services.AddScoped<AcessoService>();
        services.AddScoped<ImagemService>();
        services.AddScoped<DadosPessoaisService>();
        services.AddHostedService<ExpiracaoPedidosService>();

        return services;
    }
}
