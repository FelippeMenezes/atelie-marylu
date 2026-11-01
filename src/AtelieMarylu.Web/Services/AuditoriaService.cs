using System.Security.Claims;
using AtelieMarylu.Domain.Auditoria;
using AtelieMarylu.Web.Data;
using Microsoft.AspNetCore.Components.Authorization;

namespace AtelieMarylu.Web.Services;

public record Autor(string? Id, string Nome)
{
    public static readonly Autor Sistema = new(null, "sistema");
}

// Descobre quem está executando a ação: o usuário logado no circuito Blazor ou na requisição HTTP.
// Fora disso (webhook, tarefa em segundo plano) é o "sistema".
public class UsuarioAtual(IServiceProvider services, IHttpContextAccessor http)
{
    public async Task<Autor> ObterAsync()
    {
        ClaimsPrincipal? usuario = null;
        try
        {
            // No Blazor (interativo ou SSR) o estado de autenticação é a fonte confiável;
            // o HttpContext pode estar desatualizado dentro de um circuito.
            var provider = services.GetService<AuthenticationStateProvider>();
            if (provider is not null) usuario = (await provider.GetAuthenticationStateAsync()).User;
        }
        catch (InvalidOperationException)
        {
            // Fora do Blazor (ex.: endpoint de webhook) o provider não tem estado
        }
        if (usuario?.Identity?.IsAuthenticated != true)
            usuario = http.HttpContext?.User;

        return usuario?.Identity?.IsAuthenticated == true
            ? new Autor(usuario.FindFirstValue(ClaimTypes.NameIdentifier), usuario.Identity.Name ?? "?")
            : Autor.Sistema;
    }
}

public class AuditoriaService(UsuarioAtual usuarioAtual)
{
    // Adiciona o registro ao mesmo DbContext da alteração: grava junto, na mesma transação
    public async Task RegistrarAsync(ApplicationDbContext db, string acao, string entidade, object entidadeId, string? detalhes = null, Autor? autor = null)
    {
        autor ??= await usuarioAtual.ObterAsync();
        db.Auditoria.Add(new RegistroAuditoria
        {
            UsuarioId = autor.Id,
            Autor = autor.Nome,
            Acao = acao,
            Entidade = entidade,
            EntidadeId = entidadeId.ToString() ?? "",
            Detalhes = detalhes is { Length: > 1000 } ? detalhes[..1000] : detalhes,
        });
    }
}
