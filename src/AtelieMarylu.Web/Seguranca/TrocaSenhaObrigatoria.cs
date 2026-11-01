using System.Security.Claims;
using AtelieMarylu.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

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

// Coloca o claim "trocar_senha" no login de quem está marcado. Depois da troca, o template
// chama RefreshSignInAsync, o claim some e o acesso volta ao normal.
public class FabricaClaimsUsuario(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager, IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identidade = await base.GenerateClaimsAsync(user);
        if (user.TrocarSenhaObrigatoria)
            identidade.AddClaim(new Claim(TrocaSenhaObrigatoria.Claim, "1"));
        return identidade;
    }
}
