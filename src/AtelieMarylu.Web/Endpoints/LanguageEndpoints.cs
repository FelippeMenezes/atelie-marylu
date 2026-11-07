using AtelieMarylu.Web.Localization;
using Microsoft.AspNetCore.Localization;

namespace AtelieMarylu.Web.Endpoints;

// Guarda o idioma escolhido num cookie e volta para a mesma página
public static class LanguageEndpoints
{
    public static void MapLanguageEndpoints(this WebApplication app)
    {
        app.MapGet("/language/{code}", (string code, string? returnUrl, HttpContext context) =>
        {
            if (!SupportedLanguages.IsSupported(code))
                return Results.NotFound();

            context.Response.Cookies.Append(
                CookieRequestCultureProvider.DefaultCookieName,
                CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(code)),
                new CookieOptions
                {
                    Expires = DateTimeOffset.UtcNow.AddYears(1),
                    IsEssential = true,
                    SameSite = SameSiteMode.Lax,
                    Secure = true,
                    HttpOnly = true,
                });

            // Só volta para endereços da própria loja: evita usar a loja para redirecionar a sites falsos
            var isLocal = returnUrl is { Length: > 0 } && returnUrl[0] == '/' && !returnUrl.StartsWith("//") && !returnUrl.Contains('\\');
            return Results.LocalRedirect(isLocal ? returnUrl! : "/");
        });
    }
}
