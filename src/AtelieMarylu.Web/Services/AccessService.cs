using System.Security.Claims;
using AtelieMarylu.Domain.Auditing;
using AtelieMarylu.Domain.Security;
using AtelieMarylu.Infrastructure.Data;
using AtelieMarylu.Web.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AtelieMarylu.Web.Services;

// Administração de acesso (só o Admin usa):
// - grupo de funcionários = Role do Identity com claims "permission"
// - permissão individual = claim "permission" no próprio usuário
// Ao mudar o acesso de alguém, o security stamp é renovado para o login dele ser reavaliado.
public class AccessService(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IDbContextFactory<ApplicationDbContext> dbFactory,
    AuditService audit,
    PersonalDataService personalData,
    ILogger<AccessService> logger)
{
    public async Task<IReadOnlyList<UserSummary>> ListUsersAsync(string? search)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = SearchPattern.Contains(search);
            query = query.Where(u => EF.Functions.ILike(u.Email!, pattern, SearchPattern.EscapeCharacter)
                || EF.Functions.ILike(u.FullName, pattern, SearchPattern.EscapeCharacter));
        }
        var users = await query.OrderBy(u => u.FullName).Take(200).ToListAsync();
        var ids = users.Select(u => u.Id).ToList();

        var roles = await (from ur in db.UserRoles
                            join role in db.Roles on ur.RoleId equals role.Id
                            where ids.Contains(ur.UserId)
                            select new { ur.UserId, role.Name }).ToListAsync();
        var withPermission = await db.UserClaims
            .Where(c => ids.Contains(c.UserId) && c.ClaimType == Permissions.ClaimType)
            .Select(c => c.UserId).Distinct().ToListAsync();

        return users.Select(u =>
        {
            var names = roles.Where(p => p.UserId == u.Id).Select(p => p.Name!).ToList();
            var groups = names.Where(n => !AppRoles.IsSystemRole(n)).ToList();
            return new UserSummary(u.Id, u.Email ?? "", u.FullName, names.Contains(AppRoles.Admin),
                groups.Count > 0 || withPermission.Contains(u.Id), groups);
        }).ToList();
    }

    public async Task<UserAccessData> GetAccessAsync(string userId)
    {
        var user = await FindAsync(userId);
        var roles = await userManager.GetRolesAsync(user);
        var claims = await userManager.GetClaimsAsync(user);
        return new UserAccessData(
            roles.Contains(AppRoles.Admin),
            roles.Where(p => !AppRoles.IsSystemRole(p)).ToList(),
            claims.Where(c => c.Type == Permissions.ClaimType).Select(c => c.Value).ToList());
    }

    public async Task SaveAccessAsync(string userId, string loggedAdminId, UserAccessData access)
    {
        var user = await FindAsync(userId);
        var currentRoles = await userManager.GetRolesAsync(user);

        if (userId == loggedAdminId && !access.IsAdmin)
            throw new AccessException("You cannot remove your own Admin access.");

        // Papéis desejados: Admin (se marcado) + grupos. "Cliente" nunca é mexido aqui.
        var validGroups = roleManager.Roles.Select(role => role.Name!).ToList().Where(n => !AppRoles.IsSystemRole(n)).ToHashSet();
        var wanted = access.Groups.Where(validGroups.Contains).ToHashSet();
        if (access.IsAdmin) wanted.Add(AppRoles.Admin);

        var manageable = currentRoles.Where(p => p != AppRoles.Customer).ToList();
        EnsureSucceeded(await userManager.RemoveFromRolesAsync(user, manageable.Except(wanted)));
        EnsureSucceeded(await userManager.AddToRolesAsync(user, wanted.Except(manageable)));

        var currentClaims = (await userManager.GetClaimsAsync(user)).Where(c => c.Type == Permissions.ClaimType).ToList();
        var wantedPermissions = access.IndividualPermissions.Where(Permissions.All.ContainsKey).ToHashSet();
        EnsureSucceeded(await userManager.RemoveClaimsAsync(user, currentClaims.Where(c => !wantedPermissions.Contains(c.Value))));
        EnsureSucceeded(await userManager.AddClaimsAsync(user, wantedPermissions
            .Where(p => currentClaims.All(c => c.Value != p))
            .Select(p => new Claim(Permissions.ClaimType, p))));

        await userManager.UpdateSecurityStampAsync(user);
        await RecordAsync(AuditActions.AccessChanged, AuditEntities.User, user.Email ?? userId,
            $"Admin: {(access.IsAdmin ? "sim" : "não")} · Grupos: {string.Join(", ", wanted.Where(d => d != AppRoles.Admin))} · Individuais: {string.Join(", ", wantedPermissions)}");
    }

    // Redefine a senha de alguém pelo painel (só o Admin, pela tela /admin/users/{id}).
    // Devolve a senha temporária: a tela mostra UMA vez e ela não fica guardada em lugar nenhum.
    public async Task<string> ResetPasswordAsync(string userId, string loggedAdminId)
    {
        // A própria senha se troca em "Trocar senha", que pede a senha atual.
        // Verificado antes de ir ao banco: não precisa do usuário para isso.
        if (userId == loggedAdminId)
            throw new AccessException("You cannot reset your own password here.");

        var user = await FindAsync(userId);
        // Uma conta de Admin roubada não pode servir para tomar as contas dos outros Admins
        if (await userManager.IsInRoleAsync(user, AppRoles.Admin))
            throw new AccessException("Admin passwords cannot be reset here.");
        // Conta que só entra com Google: não há senha para redefinir
        if (!await userManager.HasPasswordAsync(user))
            throw new AccessException("This account signs in with Google and has no password to reset.");

        // Tudo marcado antes do reset: o ResetPasswordAsync salva o usuário inteiro, então a troca
        // obrigatória e o desbloqueio são gravados junto com a senha nova, num único UPDATE.
        // Quem pede ajuda costuma estar bloqueado pelas 5 tentativas erradas.
        user.MustChangePassword = true;
        user.LockoutEnd = null;
        user.AccessFailedCount = 0;

        var temporary = TemporaryPassword.Generate();
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        // Valida a senha, troca o hash e renova o security stamp (desconecta os outros aparelhos)
        EnsureSucceeded(await userManager.ResetPasswordAsync(user, token, temporary));

        // Nunca registre a senha na auditoria nem no log: quem lê a auditoria não pode conseguir entrar na conta
        await RecordAsync(AuditActions.PasswordReset, AuditEntities.User, user.Email ?? userId);
        return temporary;
    }

    // Exclui um cliente pelo painel (só o Admin, pela tela /admin/users/{id}), ex.: cadastro falso.
    // Usa a mesma exclusão do botão "Excluir conta" do próprio cliente (PersonalDataService):
    // sem pedidos, a conta é apagada; com pedidos, ela é anonimizada e os pedidos ficam (obrigação fiscal).
    // Nos dois casos o e-mail fica livre para um cadastro novo.
    public async Task DeleteCustomerAsync(string userId, string loggedAdminId, string reason, string typedEmail)
    {
        // A própria conta se exclui em Minha conta → Dados pessoais, que pede a senha
        if (userId == loggedAdminId)
            throw new AccessException("You cannot delete your own account here.");
        // O motivo vai para a auditoria: sem ele, ninguém saberia depois por que o cliente sumiu
        reason = reason.Trim();
        if (reason.Length < 3)
            throw new AccessException("Enter the reason for the deletion.");

        var user = await FindAsync(userId);
        // Digitar o e-mail evita excluir a pessoa errada por um clique distraído
        if (!string.Equals(typedEmail.Trim(), user.Email, StringComparison.OrdinalIgnoreCase))
            throw new AccessException("The email typed does not match this customer's email.");
        // Uma conta de Admin roubada não pode servir para apagar os outros Admins
        if (await userManager.IsInRoleAsync(user, AppRoles.Admin))
            throw new AccessException("Admin accounts cannot be deleted here.");
        // Funcionário com acesso ao painel: tire o acesso primeiro, para a exclusão ser uma decisão consciente
        var access = await GetAccessAsync(userId);
        if (access.Groups.Count > 0 || access.IndividualPermissions.Count > 0)
            throw new AccessException("Remove this person's admin access before deleting the account.");

        bool hadOrders;
        await using (var db = await dbFactory.CreateDbContextAsync())
            hadOrders = await db.Orders.AnyAsync(o => o.CustomerId == userId);

        EnsureSucceeded(await personalData.DeleteAccountAsync(user));

        // O registro usa o id, nunca o e-mail: guardar o e-mail desfaria a exclusão (LGPD).
        // Quem fez e quando ficam nas colunas Author e OccurredAt da auditoria.
        await RecordAsync(AuditActions.CustomerDeletedByStaff, AuditEntities.User, userId,
            $"Cliente removido pela equipe de gestão técnica. Motivo: {reason}. " +
            (hadOrders ? "Conta anonimizada; pedidos mantidos." : "Conta apagada."));
        logger.LogInformation("Customer {UserId} deleted by the technical staff (admin {AdminId}). Had orders: {HadOrders}.",
            userId, loggedAdminId, hadOrders);
    }

    public async Task<IReadOnlyList<GroupSummary>> ListGroupsAsync()
    {
        var groups = new List<GroupSummary>();
        foreach (var role in roleManager.Roles.ToList().Where(role => !AppRoles.IsSystemRole(role.Name!)).OrderBy(role => role.Name))
        {
            var claims = await roleManager.GetClaimsAsync(role);
            var members = await userManager.GetUsersInRoleAsync(role.Name!);
            groups.Add(new GroupSummary(role.Name!, claims.Where(c => c.Type == Permissions.ClaimType).Select(c => c.Value).ToList(), members.Count));
        }
        return groups;
    }

    public async Task CreateGroupAsync(string name)
    {
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name)) throw new AccessException("Enter the group name.");
        if (AppRoles.IsSystemRole(name) || await roleManager.RoleExistsAsync(name))
            throw new AccessException("A group with that name already exists.");
        EnsureSucceeded(await roleManager.CreateAsync(new IdentityRole(name)));
        await RecordAsync(AuditActions.GroupCreated, AuditEntities.Group, name);
    }

    public async Task SaveGroupPermissionsAsync(string name, IEnumerable<string> permissions)
    {
        var role = await FindGroupAsync(name);
        var current = (await roleManager.GetClaimsAsync(role)).Where(c => c.Type == Permissions.ClaimType).ToList();
        var wanted = permissions.Where(Permissions.All.ContainsKey).ToHashSet();

        foreach (var claim in current.Where(c => !wanted.Contains(c.Value)))
            EnsureSucceeded(await roleManager.RemoveClaimAsync(role, claim));
        foreach (var p in wanted.Where(p => current.All(c => c.Value != p)))
            EnsureSucceeded(await roleManager.AddClaimAsync(role, new Claim(Permissions.ClaimType, p)));

        await RefreshMembersAsync(name);
        await RecordAsync(AuditActions.GroupPermissionsChanged, AuditEntities.Group, name, string.Join(", ", wanted));
    }

    public async Task DeleteGroupAsync(string name)
    {
        var role = await FindGroupAsync(name);
        var members = await userManager.GetUsersInRoleAsync(name);
        EnsureSucceeded(await roleManager.DeleteAsync(role));
        foreach (var member in members)
            await userManager.UpdateSecurityStampAsync(member);
        await RecordAsync(AuditActions.GroupDeleted, AuditEntities.Group, name, $"{members.Count} membro(s)");
    }

    private async Task RecordAsync(string action, string entity, string id, string? details = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await audit.RecordAsync(db, action, entity, id, details);
        await db.SaveChangesAsync();
    }

    private async Task RefreshMembersAsync(string group)
    {
        foreach (var member in await userManager.GetUsersInRoleAsync(group))
            await userManager.UpdateSecurityStampAsync(member);
    }

    private async Task<ApplicationUser> FindAsync(string userId) =>
        await userManager.FindByIdAsync(userId) ?? throw new AccessException("User not found.");

    private async Task<IdentityRole> FindGroupAsync(string name)
    {
        if (AppRoles.IsSystemRole(name)) throw new AccessException("System roles cannot be changed here.");
        return await roleManager.FindByNameAsync(name) ?? throw new AccessException("Group not found.");
    }

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new AccessException(string.Join(" ", result.Errors.Select(e => e.Description)));
    }
}
