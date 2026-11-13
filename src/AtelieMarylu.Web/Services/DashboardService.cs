using AtelieMarylu.Domain.Auditing;
using AtelieMarylu.Domain.Orders;
using AtelieMarylu.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtelieMarylu.Web.Services;

public class DashboardService(IDbContextFactory<ApplicationDbContext> dbFactory, TimeProvider clock)
{
    private const int AlertWindowDays = 30;
    private const int MaxAlerts = 20;

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
                .OrderByDescending(a => a.OccurredAt).Take(MaxAlerts).ToListAsync(ct));
    }
}
