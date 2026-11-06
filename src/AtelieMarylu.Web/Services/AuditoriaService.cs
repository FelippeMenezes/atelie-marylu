using AtelieMarylu.Domain.Auditoria;
using AtelieMarylu.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtelieMarylu.Web.Services;

public class AuditoriaService(UsuarioAtual usuarioAtual, IDbContextFactory<ApplicationDbContext> dbFactory)
{
    private const int LimiteDaLista = 300;
    private const string AutorRemovido = "usuário removido";

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
            var padrao = PadraoBusca.Contem(busca);
            consulta = consulta.Where(a => EF.Functions.ILike(a.Autor, padrao, PadraoBusca.CaractereDeEscape)
                || EF.Functions.ILike(a.Detalhes!, padrao, PadraoBusca.CaractereDeEscape));
        }
        return await consulta.OrderByDescending(a => a.Quando).Take(LimiteDaLista).ToListAsync(ct);
    }

    // LGPD: na exclusão da conta, o histórico fica, mas sem o e-mail da pessoa
    public async Task AnonimizarUsuarioAsync(string usuarioId, string? email, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Auditoria.Where(a => a.UsuarioId == usuarioId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Autor, AutorRemovido), ct);
        if (!string.IsNullOrEmpty(email))
        {
            await db.Auditoria.Where(a => a.Entidade == EntidadesAuditoria.Usuario && a.EntidadeId == email)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.EntidadeId, usuarioId), ct);
        }
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
