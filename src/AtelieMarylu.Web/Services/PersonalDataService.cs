using AtelieMarylu.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AtelieMarylu.Web.Services;

// Direito de exclusão (LGPD, art. 18). Sem pedidos, a conta é apagada.
// Com pedidos, o registro da venda precisa ficar (obrigação fiscal), então a conta é anonimizada:
// some nome, e-mail, telefone, CPF, senha e acessos, e ninguém mais consegue entrar nela.
public class PersonalDataService(UserManager<ApplicationUser> userManager, IDbContextFactory<ApplicationDbContext> dbFactory, AuditService audit)
{
    // LGPD: a pessoa tem direito a todos os seus dados, inclusive pedidos e endereços
    public async Task<IReadOnlyList<object>> ExportOrdersAsync(string userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        // Poucos pedidos por cliente, cada um com poucos itens e solicitações: uma consulta só (como no OrderService)
        var orders = await db.Orders.AsNoTracking().AsSingleQuery()
            .Where(p => p.CustomerId == userId)
            .OrderBy(p => p.CreatedAt)
            .Select(p => new
            {
                p.Code, p.CreatedAt, Status = p.Status.ToString(), p.Address, p.ShippingName, p.ShippingCost, p.TrackingCode,
                Items = p.Items.Select(i => new { i.ProductName, i.Quantity, i.UnitPrice }),
                CancellationRequests = p.CancellationRequests.Select(r => new { r.Protocol, r.RequestedAt, r.Reason, r.ResolvedAt }),
            })
            .ToListAsync(ct);
        return orders;
    }

    public async Task<IdentityResult> DeleteAccountAsync(ApplicationUser user)
    {
        await audit.AnonymizeUserAsync(user.Id, user.Email);

        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            if (!await db.Orders.AnyAsync(p => p.CustomerId == user.Id))
                return await userManager.DeleteAsync(user);
        }

        // O pedido fica (obrigação fiscal), mas o motivo escrito pela cliente no cancelamento não é dado fiscal
        // e pode ter informação pessoal: sai junto com a conta
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            await db.CancellationRequests
                .Where(r => db.Orders.Any(o => o.Id == r.OrderId && o.CustomerId == user.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Reason, (string?)null));
        }

        foreach (var login in await userManager.GetLoginsAsync(user))
            await userManager.RemoveLoginAsync(user, login.LoginProvider, login.ProviderKey);
        await userManager.RemoveClaimsAsync(user, await userManager.GetClaimsAsync(user));
        await userManager.RemoveFromRolesAsync(user, await userManager.GetRolesAsync(user));
        await userManager.RemovePasswordAsync(user);

        var anonymous = $"removido-{user.Id}@anonimo.invalid";
        user.UserName = anonymous;
        user.Email = anonymous;
        user.EmailConfirmed = false;
        user.FullName = "Cliente removido";
        user.PhoneNumber = null;
        user.TaxId = null;
        user.LockoutEnabled = true;
        user.LockoutEnd = DateTimeOffset.MaxValue;

        var result = await userManager.UpdateAsync(user);
        if (result.Succeeded)
            await userManager.UpdateSecurityStampAsync(user);
        return result;
    }
}
