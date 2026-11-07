using System.Security.Claims;

namespace AtelieMarylu.Web.Security;

// O Admin inicial nasce com a senha da configuração. Até trocá-la, ele só consegue acessar a tela de troca de senha.
public static class MandatoryPasswordChange
{
    public const string Claim = "must_change_password";
    public const string PagePath = "/Account/Manage/ChangePassword";

    public static bool IsRequired(ClaimsPrincipal user) => user.HasClaim(Claim, "1");

    // Toda requisição de página de um usuário marcado é redirecionada para a troca de senha
    public static IApplicationBuilder UseMandatoryPasswordChange(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        var path = context.Request.Path;
        var allowed = path.StartsWithSegments("/Account") || path.StartsWithSegments("/_framework")
            || path.StartsWithSegments("/_blazor") || Path.HasExtension(path.Value);
        if (!allowed && IsRequired(context.User))
        {
            context.Response.Redirect(PagePath);
            return;
        }
        await next();
    });
}
