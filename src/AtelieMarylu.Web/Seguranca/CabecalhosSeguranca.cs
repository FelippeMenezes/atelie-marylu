using AtelieMarylu.Web.Configuracao;

namespace AtelieMarylu.Web.Seguranca;

// Cabeçalhos HTTP que mandam o navegador se proteger contra ataques comuns
// (clickjacking, sniffing de tipo, scripts injetados de outros domínios...).
public static class CabecalhosSeguranca
{
    private const string Cloudflare = "https://challenges.cloudflare.com";

    public static IApplicationBuilder UseCabecalhosSeguranca(this IApplicationBuilder app, bool desenvolvimento, bool turnstile,
        bool loginGoogle)
    {
        var csp = MontarCsp(desenvolvimento, turnstile, loginGoogle);

        return app.Use(async (context, next) =>
        {
            var h = context.Response.Headers;
            h.ContentSecurityPolicy = csp;
            h.XContentTypeOptions = "nosniff";
            h.XFrameOptions = "DENY";
            h["Referrer-Policy"] = "strict-origin-when-cross-origin";
            h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
            await next();
        });
    }

    public static string MontarCsp(bool desenvolvimento, bool turnstile, bool loginGoogle)
    {
        // No desenvolvimento o hot reload usa websocket em outra porta
        var connect = desenvolvimento ? "'self' ws: wss:" : "'self'";
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
            loginGoogle ? $"form-action 'self' {LoginGoogleOptions.EnderecoAutorizacao}" : "form-action 'self'",
            "frame-ancestors 'none'");
    }
}
