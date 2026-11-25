using AtelieMarylu.Domain.Auditing;
using AtelieMarylu.Domain.Orders;
using AtelieMarylu.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtelieMarylu.Web.Services;

public class DashboardService(IDbContextFactory<ApplicationDbContext> dbFactory, TimeProvider clock)
{
    private const int AlertWindowDays = 30;
    private const int MaxAlerts = 20;
    private const int MaxCancellationRequests = 50;

    public async Task<DashboardSummary> GetSummaryAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var since = clock.GetUtcNow().UtcDateTime.AddDays(-AlertWindowDays);

        return new DashboardSummary(
            await db.Orders.CountAsync(p => p.Status == OrderStatus.Paid, ct),
            await db.Orders.CountAsync(p => p.Status == OrderStatus.AwaitingPayment, ct),
            await db.Products.CountAsync(p => p.IsActive && p.Stock == 0, ct),
            await db.AuditEntries.AsNoTracking()
                .Where(a => (a.Action == AuditActions.PaymentMismatch || a.Action == AuditActions.PaymentReversed) && a.OccurredAt >= since)
                .OrderByDescending(a => a.OccurredAt).Take(MaxAlerts).ToListAsync(ct),
            // Sem janela de tempo: uma solicitação só sai da lista quando alguém responde
            // Ordena e corta antes de projetar: o EF não traduz para SQL um OrderBy sobre o construtor de um record
            await db.CancellationRequests.AsNoTracking()
                .Where(r => r.ResolvedAt == null)
                .OrderBy(r => r.RequestedAt).Take(MaxCancellationRequests)
                .Join(db.Orders, r => r.OrderId, o => o.Id,
                    (r, o) => new PendingCancellation(o.Id, o.Code, r.Protocol, r.RequestedAt, r.OrderStatusAtRequest))
                .ToListAsync(ct));
    }
}
