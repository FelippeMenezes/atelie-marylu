using AtelieMarylu.Web.Configuration;

namespace AtelieMarylu.Web.Security;

// Cabeçalhos HTTP que mandam o navegador se proteger contra ataques comuns
// (clickjacking, sniffing de tipo, scripts injetados de outros domínios...).
public static class SecurityHeaders
{
    private const string Cloudflare = "https://challenges.cloudflare.com";

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app, bool isDevelopment, bool turnstile,
        bool googleLogin)
    {
        var csp = BuildCsp(isDevelopment, turnstile, googleLogin);

        return app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers.ContentSecurityPolicy = csp;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
            await next();
        });
    }

    public static string BuildCsp(bool isDevelopment, bool turnstile, bool googleLogin)
    {
        // No desenvolvimento o hot reload usa websocket em outra porta
        var connect = isDevelopment ? "'self' ws: wss:" : "'self'";
        // O Turnstile (anti-robô) carrega um script e um iframe do Cloudflare
        var scripts = turnstile ? $"'self' {Cloudflare}" : "'self'";
        var frames = turnstile ? Cloudflare : "'none'";
        return string.Join("; ",
            "default-src 'self'",
            $"script-src {scripts}",
            // 'unsafe-inline' só para estilos: o Blazor e o Bootstrap usam atributos style
            "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com",
            "font-src 'self' https://fonts.gstatic.com",
            "img-src 'self' data:",
            $"connect-src {connect}",
            $"frame-src {frames}",
            "object-src 'none'",
            "base-uri 'self'",
            // O botão "Continuar com Google" envia um formulário que é redirecionado ao provedor
            googleLogin ? $"form-action 'self' {GoogleLoginOptions.AuthorizationEndpoint}" : "form-action 'self'",
            "frame-ancestors 'none'");
    }
}
