using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Formatting.Compact;
using AtelieMarylu.Domain.Frete;
using AtelieMarylu.Domain.Pagamentos;
using AtelieMarylu.Domain.Seguranca;
using AtelieMarylu.Web.Components;
using AtelieMarylu.Web.Components.Account;
using AtelieMarylu.Web.Configuracao;
using AtelieMarylu.Web.Data;
using AtelieMarylu.Web.Integracoes;
using AtelieMarylu.Web.Seguranca;
using AtelieMarylu.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Logs estruturados: legíveis no desenvolvimento, JSON em produção (fácil de pesquisar e alertar)
builder.Services.AddSerilog((services, log) =>
{
    log.ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext();
    if (builder.Environment.IsDevelopment()) log.WriteTo.Console();
    else log.WriteTo.Console(new RenderedCompactJsonFormatter());
});

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();
builder.Services.AddHttpContextAccessor();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

// Cada permissão vira uma policy: passa quem é Admin ou tem o claim "permissao" correspondente
// (direto no usuário ou herdado de um grupo).
builder.Services.AddAuthorization(options =>
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

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
// A factory permite criar um DbContext curto por operação, o recomendado no Blazor Server
// (um mesmo circuito pode disparar operações em paralelo). Também registra o DbContext
// como scoped, que é o que o Identity usa.
builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString), ServiceLifetime.Scoped);
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        // Sem serviço de e-mail por enquanto; quando houver, voltar para true
        options.SignIn.RequireConfirmedAccount = false;
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 8;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

// Substitui a fábrica padrão para incluir o claim de troca de senha obrigatória
builder.Services.AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>, FabricaClaimsUsuario>();

// Reavalia o login a cada minuto: ao remover o acesso de um funcionário, ele perde o painel em até 1 min
builder.Services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.FromMinutes(1));

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;                           // JavaScript não lê o cookie de login
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;  // só trafega por HTTPS
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});

builder.Services.AddDataProtection()
    .SetApplicationName("AtelieMarylu")
    .PersistKeysToDbContext<ApplicationDbContext>();

// Limita tentativas por IP nas telas de conta (login, cadastro, senha) e no webhook
builder.Services.AddRateLimiter(options =>
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

// Em produção o site roda atrás de um proxy (HTTPS termina nele). Só confiamos no cabeçalho
// X-Forwarded-For vindo das redes listadas em Proxy:RedesConfiaveis (ex.: "10.0.0.0/8");
// sem isso, todo mundo pareceria ter o IP do proxy e o limite de tentativas valeria para a loja inteira.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var rede in builder.Configuration.GetSection("Proxy:RedesConfiaveis").Get<string[]>() ?? [])
        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(rede));
});

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

builder.Services.AddOptions<LojaOptions>().Bind(builder.Configuration.GetSection(LojaOptions.Secao)).ValidateDataAnnotations().ValidateOnStart();
builder.Services.Configure<TurnstileOptions>(builder.Configuration.GetSection(TurnstileOptions.Secao));

// Integrações externas: trocar estas duas linhas pelas implementações reais
// (Melhor Envio e Mercado Pago) não exige mudar nenhuma tela.
builder.Services.AddSingleton<ICalculadoraFrete, FreteSimulado>();
builder.Services.AddSingleton<IGatewayPagamento, PagamentoSimulado>();

builder.Services.AddHttpClient<ConsultaCep>(http =>
{
    http.BaseAddress = new Uri("https://viacep.com.br/");
    http.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddHttpClient<Turnstile>(http =>
{
    http.BaseAddress = new Uri("https://challenges.cloudflare.com/");
    http.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<PedidoEventos>();
builder.Services.AddScoped<UsuarioAtual>();
builder.Services.AddScoped<AuditoriaService>();
builder.Services.AddScoped<CarrinhoService>();
builder.Services.AddScoped<PedidoService>();
builder.Services.AddScoped<AcessoService>();
builder.Services.AddScoped<ImagemService>();
builder.Services.AddScoped<DadosPessoaisService>();
builder.Services.AddHostedService<ExpiracaoPedidosService>();

builder.Services.AddHealthChecks().AddDbContextCheck<ApplicationDbContext>("banco");

var culturaBr = new CultureInfo("pt-BR");
CultureInfo.DefaultThreadCurrentCulture = culturaBr;
CultureInfo.DefaultThreadCurrentUICulture = culturaBr;

var app = builder.Build();

// Em produção, aceitar qualquer Host abre espaço para ataques de "host header"
if (!app.Environment.IsDevelopment() && app.Configuration["AllowedHosts"] is null or "*")
    throw new InvalidOperationException("Defina AllowedHosts com o domínio da loja (ex.: ateliemarylu.com.br) antes de rodar em produção.");

await SeedDados.ExecutarAsync(app.Services, app.Environment.IsDevelopment());

// Configure the HTTP request pipeline.
app.UseForwardedHeaders();
app.UseSerilogRequestLogging();
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseRequestLocalization("pt-BR");
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseCabecalhosSeguranca(app.Environment.IsDevelopment(), app.Services.GetRequiredService<IOptions<TurnstileOptions>>().Value.Ativo);
app.UseRateLimiter();

// Arquivos enviados em tempo de execução (fotos de produtos)
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseTrocaSenhaObrigatoria();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();

app.MapWebhooksPagamento();
app.MapSeo();
app.MapHealthChecks("/health");

app.Run();
