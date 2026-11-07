using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace AtelieMarylu.Web.Services;

// Descobre quem está executando a ação: o usuário logado no circuito Blazor ou na requisição HTTP.
// Fora disso (webhook, tarefa em segundo plano) é o "sistema".
public class CurrentUserService(IServiceProvider services, IHttpContextAccessor http)
{
    public async Task<Author> GetAsync()
    {
        ClaimsPrincipal? user = null;
        try
        {
            // No Blazor (interativo ou SSR) o estado de autenticação é a fonte confiável;
            // o HttpContext pode estar desatualizado dentro de um circuito.
            var provider = services.GetService<AuthenticationStateProvider>();
            if (provider is not null) user = (await provider.GetAuthenticationStateAsync()).User;
        }
        catch (InvalidOperationException)
        {
            // Fora do Blazor (ex.: endpoint de webhook) o provider não tem estado
        }
        if (user?.Identity?.IsAuthenticated != true)
            user = http.HttpContext?.User;

        return user?.Identity?.IsAuthenticated == true
            ? new Author(user.FindFirstValue(ClaimTypes.NameIdentifier), user.Identity.Name ?? "?")
            : Author.Automated;
    }
}
