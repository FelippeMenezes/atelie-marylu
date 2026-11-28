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
            // E-mails da outbox para esta cliente: o dado pessoal sai já, a prova fica (a mesma regra da limpeza de
            // 30 dias do EmailOutboxDispatcher). Os pedidos ficam por obrigação fiscal; a prova de que a loja mandou a
            // confirmação exigida pelo Decreto 7.962 também é obrigação legal, e a LGPD permite guardá-la (art. 16, I),
            // desde que sem dado pessoal. Dois critérios, porque ela pode ter trocado de e-mail depois de um pedido:
            // os mandados ao e-mail atual e os de qualquer pedido dela. O aviso para a loja não tem dados dela e fica.
            var email = user.Email ?? "";
            var ofThisCustomer = db.OutboxEmails
                .Where(e => (email != "" && e.Recipient == email)
                    || (e.Kind != OutboxEmailKinds.CancellationStaffNotice
                        && db.Orders.Any(o => o.Id == e.OrderId && o.CustomerId == user.Id)));

            // 1) Pendentes: não faz sentido mandar e-mail para uma conta excluída. Viram "puladas", com o motivo.
            await ofThisCustomer
                .Where(e => e.SentAt == null && e.FailedAt == null && e.SkippedAt == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.SkippedAt, DateTime.UtcNow)
                    .SetProperty(e => e.Recipient, (string?)null)
                    .SetProperty(e => e.Payload, "{}")
                    .SetProperty(e => e.LastError, "Skipped: account deleted"));

            // 2) Encerradas: perdem destinatário, payload e erro; tipo, pedido e datas ficam como prova
            await ofThisCustomer
                .Where(e => e.Recipient != null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.Recipient, (string?)null)
                    .SetProperty(e => e.Payload, "{}")
                    .SetProperty(e => e.LastError, (string?)null));

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
