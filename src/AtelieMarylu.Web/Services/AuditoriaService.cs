using AtelieMarylu.Domain.Auditoria;
using AtelieMarylu.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtelieMarylu.Web.Services;

public class AuditoriaService(UsuarioAtual usuarioAtual, IDbContextFactory<ApplicationDbContext> dbFactory)
{
    private const int LimiteDaLista = 300;

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

    public async Task<List<RegistroAuditoria>> ListarAsync(string? entidade, string? busca, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var consulta = db.Auditoria.AsNoTracking();
        if (!string.IsNullOrEmpty(entidade)) consulta = consulta.Where(a => a.Entidade == entidade);
        if (!string.IsNullOrWhiteSpace(busca))
        {
            var termo = $"%{busca.Trim()}%";
            consulta = consulta.Where(a => EF.Functions.ILike(a.Autor, termo) || EF.Functions.ILike(a.Detalhes!, termo));
        }
        return await consulta.OrderByDescending(a => a.Quando).Take(LimiteDaLista).ToListAsync(ct);
    }

    // Histórico mostrado na página do pedido
    public async Task<List<RegistroAuditoria>> ListarDoPedidoAsync(int pedidoId, CancellationToken ct = default)
    {
        var entidadeId = pedidoId.ToString();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Auditoria.AsNoTracking()
            .Where(a => a.Entidade == EntidadesAuditoria.Pedido && a.EntidadeId == entidadeId)
            .OrderByDescending(a => a.Quando)
            .ToListAsync(ct);
    }
}
