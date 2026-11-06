using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using AtelieMarylu.Web.Configuracao;
using AtelieMarylu.Web.Components.Account.Pages;
using AtelieMarylu.Web.Components.Account.Pages.Manage;
using AtelieMarylu.Infrastructure.Data;
using AtelieMarylu.Web.Services;

namespace Microsoft.AspNetCore.Routing;

internal static class IdentityComponentsEndpointRouteBuilderExtensions
{
    // These endpoints are required by the Identity Razor components defined in the /Components/Account/Pages directory of this project.
    public static IEndpointConventionBuilder MapAdditionalIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var accountGroup = endpoints.MapGroup("/Account");

        // Botão "Continuar com Google": leva o navegador à tela de login do Google,
        // que depois volta para /Account/LoginGoogle
        accountGroup.MapPost("/EntrarComGoogle", (
            HttpContext context,
            [FromServices] SignInManager<ApplicationUser> signInManager,
            [FromServices] IOptions<LoginGoogleOptions> opcoesGoogle,
            [FromForm] string? returnUrl) =>
        {
            if (!opcoesGoogle.Value.Ativo)
                return Results.NotFound();

            IEnumerable<KeyValuePair<string, StringValues>> query = [
                new("ReturnUrl", returnUrl),
                new("Action", LoginGoogle.LoginCallbackAction)];

            var redirectUrl = UriHelper.BuildRelative(
                context.Request.PathBase,
                "/Account/LoginGoogle",
                QueryString.Create(query));

            var properties = signInManager.ConfigureExternalAuthenticationProperties(GoogleDefaults.AuthenticationScheme, redirectUrl);
            return TypedResults.Challenge(properties, [GoogleDefaults.AuthenticationScheme]);
        });

        accountGroup.MapPost("/Logout", async (
            ClaimsPrincipal user,
            [FromServices] SignInManager<ApplicationUser> signInManager,
            [FromForm] string returnUrl) =>
        {
            await signInManager.SignOutAsync();
            return TypedResults.LocalRedirect($"~/{returnUrl}");
        });

        var manageGroup = accountGroup.MapGroup("/Manage").RequireAuthorization();

        // "Conectar conta Google" em Minha conta → Conta Google: mesma ida ao Google, mas a volta
        // acrescenta o login à conta de quem já está logado
        manageGroup.MapPost("/ConectarGoogle", async (
            HttpContext context,
            [FromServices] SignInManager<ApplicationUser> signInManager,
            [FromServices] IOptions<LoginGoogleOptions> opcoesGoogle) =>
        {
            if (!opcoesGoogle.Value.Ativo)
                return Results.NotFound();

            // Limpa um login com Google anterior que tenha ficado pela metade
            await context.SignOutAsync(IdentityConstants.ExternalScheme);

            var redirectUrl = UriHelper.BuildRelative(
                context.Request.PathBase,
                "/Account/Manage/Google",
                QueryString.Create("Action", ContaGoogle.ConectarCallbackAction));

            var properties = signInManager.ConfigureExternalAuthenticationProperties(
                GoogleDefaults.AuthenticationScheme, redirectUrl, signInManager.UserManager.GetUserId(context.User));
            return TypedResults.Challenge(properties, [GoogleDefaults.AuthenticationScheme]);
        });

        var loggerFactory = endpoints.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var downloadLogger = loggerFactory.CreateLogger("DownloadPersonalData");

        manageGroup.MapPost("/DownloadPersonalData", async (
            HttpContext context,
            [FromServices] UserManager<ApplicationUser> userManager,
            [FromServices] DadosPessoaisService dadosPessoais,
            [FromServices] AuthenticationStateProvider authenticationStateProvider) =>
        {
            var user = await userManager.GetUserAsync(context.User);
            if (user is null)
            {
                return Results.NotFound($"Unable to load user with ID '{userManager.GetUserId(context.User)}'.");
            }

            var userId = await userManager.GetUserIdAsync(user);
            downloadLogger.LogInformation("User with ID '{UserId}' asked for their personal data.", userId);

            // Only include personal data for download
            var personalData = new Dictionary<string, string>();
            var personalDataProps = typeof(ApplicationUser).GetProperties().Where(
                prop => Attribute.IsDefined(prop, typeof(PersonalDataAttribute)));
            foreach (var p in personalDataProps)
            {
                personalData.Add(p.Name, p.GetValue(user)?.ToString() ?? "null");
            }

            var logins = await userManager.GetLoginsAsync(user);
            foreach (var l in logins)
            {
                personalData.Add($"{l.LoginProvider} external login provider key", l.ProviderKey);
            }

            personalData.Add("Authenticator Key", (await userManager.GetAuthenticatorKeyAsync(user))!);

            var pedidos = await dadosPessoais.ExportarPedidosAsync(userId);
            var fileBytes = JsonSerializer.SerializeToUtf8Bytes(new { Conta = personalData, Pedidos = pedidos },
                new JsonSerializerOptions { WriteIndented = true });

            context.Response.Headers.TryAdd("Content-Disposition", "attachment; filename=meus-dados-atelie-marylu.json");
            return TypedResults.File(fileBytes, contentType: "application/json", fileDownloadName: "meus-dados-atelie-marylu.json");
        });

        return accountGroup;
    }
}
