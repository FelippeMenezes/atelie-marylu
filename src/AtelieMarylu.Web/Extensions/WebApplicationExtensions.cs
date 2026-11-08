using AtelieMarylu.Infrastructure.Integrations;
using AtelieMarylu.Web.Components;
using AtelieMarylu.Web.Configuration;
using AtelieMarylu.Web.Endpoints;
using AtelieMarylu.Web.Security;
using Microsoft.Extensions.Options;
using Serilog;

namespace AtelieMarylu.Web.Extensions;

public static class WebApplicationExtensions
{
    // Em produção, aceitar qualquer Host abre espaço para ataques de "host header"
    public static void EnsureAllowedHostsIsConfigured(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment() && app.Configuration["AllowedHosts"] is null or "*")
            throw new InvalidOperationException("Defina AllowedHosts com o domínio da loja (ex.: ateliemarylu.com.br) antes de rodar em produção.");
    }

    // O "pipeline": o caminho que toda requisição percorre. A ordem das linhas importa.
    public static WebApplication UseStorePipeline(this WebApplication app)
    {
        app.UseForwardedHeaders();
        app.UseSerilogRequestLogging();
        if (app.Environment.IsDevelopment())
        {
            app.UseMigrationsEndPoint();
        }
        else
        {
            app.UseExceptionHandler("/Error", createScopeForErrors: true);
            app.UseHsts();
        }
        app.UseRequestLocalization();
        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
        app.UseHttpsRedirection();
        app.UseSecurityHeaders(
            app.Environment.IsDevelopment(),
            app.Services.GetRequiredService<IOptions<TurnstileOptions>>().Value.IsActive,
            app.Services.GetRequiredService<IOptions<GoogleLoginOptions>>().Value.IsActive);
        app.UseRateLimiter();

        // Arquivos enviados em tempo de execução (fotos de produtos)
        app.UseStaticFiles();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMandatoryPasswordChange();
        app.UseAntiforgery();

        return app;
    }

    public static WebApplication MapStoreEndpoints(this WebApplication app)
    {
        app.MapStaticAssets();
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        // Endpoints usados pelas telas de conta em /Account
        app.MapAdditionalIdentityEndpoints();

        app.MapPaymentWebhooks();
        app.MapSeo();
        app.MapLanguageEndpoints();
        app.MapHealthChecks("/health");
        app.MapOldAddressRedirects();

        return app;
    }
}
