using System.Threading.RateLimiting;
using AtelieMarylu.Domain.Security;
using AtelieMarylu.Infrastructure.Data;
using AtelieMarylu.Web.Components.Account;
using AtelieMarylu.Web.Configuration;
using AtelieMarylu.Web.Localization;
using AtelieMarylu.Web.Security;
using AtelieMarylu.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
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

        // O idioma vem só do cookie da escolha da pessoa. Sem cookie, o site abre em português.
        services.AddLocalization(options => options.ResourcesPath = "Resources");
        services.Configure<RequestLocalizationOptions>(options =>
        {
            options.SetDefaultCulture(SupportedLanguages.Default)
                .AddSupportedCultures(SupportedLanguages.Codes)
                .AddSupportedUICultures(SupportedLanguages.Codes);
            options.RequestCultureProviders = [new CookieRequestCultureProvider()];
        });

        services.AddCascadingAuthenticationState();
        services.AddScoped<IdentityRedirectManager>();
        services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();
        services.AddHttpContextAccessor();
        services.AddDatabaseDeveloperPageExceptionFilter();
        services.AddHealthChecks().AddDbContextCheck<ApplicationDbContext>("database");

        return services;
    }

    public static IServiceCollection AddStoreAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var authentication = services.AddAuthentication(options =>
        {
            options.DefaultScheme = IdentityConstants.ApplicationScheme;
            options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
        });
        authentication.AddIdentityCookies();

        // Entrar com Google: só é ligado se tiver as chaves na configuração
        var googleSection = configuration.GetSection(GoogleLoginOptions.SectionName);
        services.Configure<GoogleLoginOptions>(googleSection);
        var googleLogin = googleSection.Get<GoogleLoginOptions>() ?? new();
        if (googleLogin.IsActive)
        {
            authentication.AddGoogle(options =>
            {
                options.ClientId = googleLogin.ClientId;
                options.ClientSecret = googleLogin.ClientSecret;
                // Usado para recusar e-mails que o Google não confirmou (veja GoogleLogin.razor)
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
            // Tokens de uso único do Identity (redefinição de senha). Serão usados também no "esqueci minha senha" da Fase 4.
            .AddDefaultTokenProviders()
            .AddErrorDescriber<LocalizedIdentityErrorDescriber>();

        // Substitui a fábrica padrão para incluir o claim de troca de senha obrigatória
        services.AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>, AppUserClaimsPrincipalFactory>();

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

        return services;
    }

    // Cada permissão vira uma policy: passa quem é Admin ou tem o claim "permission" correspondente
    // (direto no usuário ou herdado de um grupo).
    public static IServiceCollection AddStoreAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            foreach (var permission in Permissions.All.Keys)
            {
                options.AddPolicy(permission, policy => policy.RequireAssertion(ctx =>
                    ctx.User.IsInRole(AppRoles.Admin) || ctx.User.HasClaim(Permissions.ClaimType, permission)));
            }
            options.AddPolicy(Permissions.AccessAdmin, policy => policy.RequireAssertion(ctx =>
                ctx.User.IsInRole(AppRoles.Admin) || ctx.User.HasClaim(c => c.Type == Permissions.ClaimType)));
            options.AddPolicy(AppRoles.Admin, policy => policy.RequireRole(AppRoles.Admin));
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
                var path = context.Request.Path;
                var sensitive = HttpMethods.IsPost(context.Request.Method)
                    && (path.StartsWithSegments("/Account") || path.StartsWithSegments("/api/webhooks"));
                if (!sensitive)
                    return RateLimitPartition.GetNoLimiter("unlimited");

                var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
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
    // X-Forwarded-For vindo das redes listadas em Proxy:TrustedNetworks (ex.: "10.0.0.0/8");
    // sem isso, todo mundo pareceria ter o IP do proxy e o limite de tentativas valeria para a loja inteira.
    public static IServiceCollection AddReverseProxySupport(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            foreach (var network in configuration.GetSection("Proxy:TrustedNetworks").Get<string[]>() ?? [])
                options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
        });

        return services;
    }

    public static IServiceCollection AddStoreServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ShopOptions>().Bind(configuration.GetSection(ShopOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<OrderEvents>();
        services.AddScoped<CurrentUserService>();
        services.AddScoped<AuditService>();
        services.AddScoped<CartService>();
        services.AddScoped<OrderService>();
        services.AddScoped<ProductService>();
        services.AddScoped<CustomerService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<AccessService>();
        services.AddScoped<ImageService>();
        services.AddScoped<PersonalDataService>();
        services.AddHostedService<OrderExpirationService>();

        return services;
    }
}
