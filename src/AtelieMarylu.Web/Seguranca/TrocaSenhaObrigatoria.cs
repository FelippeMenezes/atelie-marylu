using System.Security.Claims;

namespace AtelieMarylu.Web.Seguranca;

// O Admin inicial nasce com a senha da configuração. Até trocá-la, ele só consegue acessar a tela de troca de senha.
public static class TrocaSenhaObrigatoria
{
    public const string Claim = "trocar_senha";
    public const string Pagina = "/Account/Manage/ChangePassword";

    public static bool Precisa(ClaimsPrincipal usuario) => usuario.HasClaim(Claim, "1");

    // Toda requisição de página de um usuário marcado é redirecionada para a troca de senha
    public static IApplicationBuilder UseTrocaSenhaObrigatoria(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        var caminho = context.Request.Path;
        var liberado = caminho.StartsWithSegments("/Account") || caminho.StartsWithSegments("/_framework")
            || caminho.StartsWithSegments("/_blazor") || Path.HasExtension(caminho.Value);
        if (!liberado && Precisa(context.User))
        {
            context.Response.Redirect(Pagina);
            return;
        }
        await next();
    });
}
