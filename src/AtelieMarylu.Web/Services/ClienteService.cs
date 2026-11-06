using AtelieMarylu.Domain.Seguranca;
using AtelieMarylu.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtelieMarylu.Web.Services;

// Consultas de clientes usadas pelo painel
public class ClienteService(IDbContextFactory<ApplicationDbContext> dbFactory)
{
    private const int LimiteDaLista = 200;

    public async Task<List<ResumoCliente>> ListarAsync(string? busca, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var consulta = ConsultarClientes(db);
        if (!string.IsNullOrWhiteSpace(busca))
        {
            var termo = $"%{busca.Trim()}%";
            consulta = consulta.Where(u => EF.Functions.ILike(u.Email!, termo) || EF.Functions.ILike(u.NomeCompleto, termo));
        }
        return await consulta
            .OrderBy(u => u.NomeCompleto)
            .Take(LimiteDaLista)
            .Select(u => new ResumoCliente(u.Id, u.NomeCompleto, u.Email, u.PhoneNumber, u.CriadoEm, db.Pedidos.Count(p => p.ClienteId == u.Id)))
            .ToListAsync(ct);
    }

    // Para escolher o cliente ao criar um pedido pelo painel
    public async Task<List<ApplicationUser>> ListarTodosAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await ConsultarClientes(db).OrderBy(u => u.NomeCompleto).ToListAsync(ct);
    }

    public async Task<ApplicationUser?> ObterAsync(string id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
    }

    // Nome de cada cliente, para mostrar na lista de pedidos
    public async Task<Dictionary<string, string>> ObterNomesAsync(IEnumerable<string> ids, CancellationToken ct = default)
    {
        var lista = ids.Distinct().ToList();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Users.AsNoTracking()
            .Where(u => lista.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => string.IsNullOrEmpty(u.NomeCompleto) ? u.Email ?? "" : u.NomeCompleto, ct);
    }

    private static IQueryable<ApplicationUser> ConsultarClientes(ApplicationDbContext db) =>
        db.Users.AsNoTracking().Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id
            && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == Papeis.Cliente)));
}
