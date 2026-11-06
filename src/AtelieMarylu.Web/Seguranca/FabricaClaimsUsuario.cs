using System.Security.Claims;
using AtelieMarylu.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace AtelieMarylu.Web.Seguranca;

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
