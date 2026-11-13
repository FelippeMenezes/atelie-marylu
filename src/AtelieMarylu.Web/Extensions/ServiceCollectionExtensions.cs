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
        services.AddScoped<AccountEmailLinks>();
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
                // Decisão do AML-0027: a loja não exige confirmar o e-mail (atrapalharia a compra e faria o
                // cadastro depender do Resend). O cadastro só manda um e-mail de boas-vindas.
                options.SignIn.RequireConfirmedAccount = false;
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 8;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                // A troca de e-mail usa um gerador com prazo próprio (veja AccountTokens.cs)
                options.Tokens.EmailConfirmationTokenProvider = EmailConfirmationTokenProvider.ProviderName;
                options.Tokens.ChangeEmailTokenProvider = EmailConfirmationTokenProvider.ProviderName;
                // Version2: sem a tabela de passkeys (login sem senha), que a loja não usa
                options.Stores.SchemaVersion = IdentitySchemaVersions.Version2;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddSignInManager()
            // Tokens dos links: redefinição de senha (gerador padrão) e confirmação/troca de e-mail (gerador próprio)
            .AddDefaultTokenProviders()
            .AddTokenProvider<EmailConfirmationTokenProvider>(EmailConfirmationTokenProvider.ProviderName)
            .AddErrorDescriber<LocalizedIdentityErrorDescriber>();

        // Prazo dos links enviados por e-mail (veja AccountTokenLifespans)
        services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = AccountTokenLifespans.PasswordReset);
        services.Configure<EmailConfirmationTokenProviderOptions>(options => options.TokenLifespan = AccountTokenLifespans.EmailConfirmation);

        // Monta os e-mails da conta (texto, idioma) e entrega pelo IEmailClient registrado no AddInfrastructure
        services.AddScoped<AccountEmailSender>();
        services.AddScoped<IEmailSender<ApplicationUser>>(sp => sp.GetRequiredService<AccountEmailSender>());

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

            // Sem corpo, o 429 era reexecutado como a página "não encontrada" (UseStatusCodePagesWithReExecute),
            // que recusa POST: login, cadastro e webhook recebiam 400 em vez de "muitas tentativas".
            // Aqui a reexecução é desligada, o Retry-After diz em quantos segundos tentar de novo, e a mensagem é traduzida.
            options.OnRejected = async (context, ct) =>
            {
                var http = context.HttpContext;
                if (http.Features.Get<Microsoft.AspNetCore.Diagnostics.IStatusCodePagesFeature>() is { } statusPages)
                    statusPages.Enabled = false;
                http.Response.Headers.RetryAfter = "60";
                var localizer = http.RequestServices.GetService<Microsoft.Extensions.Localization.IStringLocalizer<SharedResource>>();
                http.Response.ContentType = "text/plain; charset=utf-8";
                await http.Response.WriteAsync(localizer?["Too many attempts. Wait a minute and try again."] ?? "Too many attempts. Wait a minute and try again.", ct);
            };
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var path = context.Request.Path;
                if (!HttpMethods.IsPost(context.Request.Method))
                    return RateLimitPartition.GetNoLimiter("unlimited");

                var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                // Webhook de pagamento: limite próprio e bem maior. Os avisos do provedor saem de poucos IPs, e um
                // pico de vendas não pode receber 429 (o pagamento atrasaria). Afrouxar é seguro porque cada aviso
                // precisa de assinatura válida. A chave separada ("webhook:") impede que o login e o webhook
                // dividam o mesmo contador.
                if (path.StartsWithSegments("/api/webhooks"))
                    return RateLimitPartition.GetFixedWindowLimiter($"webhook:{ip}", _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 120,
                        Window = TimeSpan.FromMinutes(1),
                    });

                // Login, cadastro, senha: limite apertado contra tentativa e erro
                if (path.StartsWithSegments("/Account"))
                    return RateLimitPartition.GetFixedWindowLimiter($"account:{ip}", _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                    });

                return RateLimitPartition.GetNoLimiter("unlimited");
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
        services.AddScoped<AccountRecoveryService>();
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

        // Fecha no provedor, em segundo plano, as cobranças que deixaram de valer (o webhook responde sem esperar)
        services.AddSingleton<ChargeClosingQueue>();
        services.AddSingleton<IChargeCloser>(sp => sp.GetRequiredService<ChargeClosingQueue>());
        services.AddHostedService<ChargeClosingService>();

        return services;
    }
}
