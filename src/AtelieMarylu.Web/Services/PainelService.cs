using AtelieMarylu.Domain.Auditoria;
using AtelieMarylu.Domain.Pedidos;
using AtelieMarylu.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtelieMarylu.Web.Services;

public class PainelService(IDbContextFactory<ApplicationDbContext> dbFactory, TimeProvider relogio)
{
    private const int DiasDeAlertas = 30;
    private const int MaximoDeAlertas = 20;

    public async Task<ResumoPainel> ObterResumoAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var desde = relogio.GetUtcNow().UtcDateTime.AddDays(-DiasDeAlertas);

        return new ResumoPainel(
            await db.Pedidos.CountAsync(p => p.Status == StatusPedido.Pago, ct),
            await db.Pedidos.CountAsync(p => p.Status == StatusPedido.AguardandoPagamento, ct),
            await db.Produtos.CountAsync(p => p.Ativo && p.Estoque == 0, ct),
            await db.Auditoria.AsNoTracking()
                .Where(a => a.Acao == AcoesAuditoria.PagamentoDivergente && a.Quando >= desde)
                .OrderByDescending(a => a.Quando).Take(MaximoDeAlertas).ToListAsync(ct));
    }
}
