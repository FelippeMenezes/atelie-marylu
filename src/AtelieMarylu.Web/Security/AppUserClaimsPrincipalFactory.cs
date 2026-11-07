using System.Security.Claims;
using AtelieMarylu.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace AtelieMarylu.Web.Security;

// Coloca o claim "must_change_password" no login de quem está marcado. Depois da troca, o template
// chama RefreshSignInAsync, o claim some e o acesso volta ao normal.
public class AppUserClaimsPrincipalFactory(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager, IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        if (user.MustChangePassword)
            identity.AddClaim(new Claim(MandatoryPasswordChange.Claim, "1"));
        return identity;
    }
}
