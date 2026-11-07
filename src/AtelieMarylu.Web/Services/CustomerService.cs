using AtelieMarylu.Domain.Security;
using AtelieMarylu.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtelieMarylu.Web.Services;

// Consultas de clientes usadas pelo painel
public class CustomerService(IDbContextFactory<ApplicationDbContext> dbFactory)
{
    private const int ListLimit = 200;

    public async Task<List<CustomerSummary>> ListAsync(string? search, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = QueryCustomers(db);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = SearchPattern.Contains(search);
            query = query.Where(u => EF.Functions.ILike(u.Email!, pattern, SearchPattern.EscapeCharacter)
                || EF.Functions.ILike(u.FullName, pattern, SearchPattern.EscapeCharacter));
        }
        return await query
            .OrderBy(u => u.FullName)
            .Take(ListLimit)
            .Select(u => new CustomerSummary(u.Id, u.FullName, u.Email, u.PhoneNumber, u.CreatedAt, db.Orders.Count(p => p.CustomerId == u.Id)))
            .ToListAsync(ct);
    }

    // Para escolher o cliente ao criar um pedido pelo painel
    public async Task<List<CustomerData>> ListForSelectionAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await QueryCustomers(db)
            .OrderBy(u => u.FullName)
            .Select(u => new CustomerData(u.Id, u.FullName, u.Email, u.PhoneNumber, u.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<CustomerData?> GetAsync(string id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Users.AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new CustomerData(u.Id, u.FullName, u.Email, u.PhoneNumber, u.CreatedAt))
            .FirstOrDefaultAsync(ct);
    }

    // Nome de cada cliente, para mostrar na lista de pedidos
    public async Task<Dictionary<string, string>> GetNamesAsync(IEnumerable<string> ids, CancellationToken ct = default)
    {
        var list = ids.Distinct().ToList();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Users.AsNoTracking()
            .Where(u => list.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => string.IsNullOrEmpty(u.FullName) ? u.Email ?? "" : u.FullName, ct);
    }

    private static IQueryable<ApplicationUser> QueryCustomers(ApplicationDbContext db) =>
        db.Users.AsNoTracking().Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id
            && db.Roles.Any(role => role.Id == ur.RoleId && role.Name == AppRoles.Customer)));
}
