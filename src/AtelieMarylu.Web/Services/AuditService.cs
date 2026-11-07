using AtelieMarylu.Domain.Auditing;
using AtelieMarylu.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtelieMarylu.Web.Services;

public class AuditService(CurrentUserService currentUser, IDbContextFactory<ApplicationDbContext> dbFactory)
{
    private const int ListLimit = 300;
    private const string RemovedAuthor = "removed user";

    // Adiciona o registro ao mesmo DbContext da alteração: grava junto, na mesma transação
    public async Task RecordAsync(ApplicationDbContext db, string action, string entity, object entityId, string? details = null, Author? author = null)
    {
        author ??= await currentUser.GetAsync();
        db.AuditEntries.Add(new AuditEntry
        {
            UserId = author.Id,
            Author = author.Name,
            Action = action,
            Entity = entity,
            EntityId = entityId.ToString() ?? "",
            Details = details is { Length: > 1000 } ? details[..1000] : details,
        });
    }

    public async Task<List<AuditEntry>> ListAsync(string? entity, string? search, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.AuditEntries.AsNoTracking();
        if (!string.IsNullOrEmpty(entity)) query = query.Where(a => a.Entity == entity);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = SearchPattern.Contains(search);
            query = query.Where(a => EF.Functions.ILike(a.Author, pattern, SearchPattern.EscapeCharacter)
                || EF.Functions.ILike(a.Details!, pattern, SearchPattern.EscapeCharacter));
        }
        return await query.OrderByDescending(a => a.OccurredAt).Take(ListLimit).ToListAsync(ct);
    }

    // LGPD: na exclusão da conta, o histórico fica, mas sem o e-mail da pessoa
    public async Task AnonymizeUserAsync(string userId, string? email, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.AuditEntries.Where(a => a.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Author, RemovedAuthor), ct);
        if (!string.IsNullOrEmpty(email))
        {
            await db.AuditEntries.Where(a => a.Entity == AuditEntities.User && a.EntityId == email)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.EntityId, userId), ct);
        }
    }

    // Histórico mostrado na página do pedido
    public async Task<List<AuditEntry>> ListForOrderAsync(int orderId, CancellationToken ct = default)
    {
        var entityId = orderId.ToString();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.AuditEntries.AsNoTracking()
            .Where(a => a.Entity == AuditEntities.Order && a.EntityId == entityId)
            .OrderByDescending(a => a.OccurredAt)
            .ToListAsync(ct);
    }
}
