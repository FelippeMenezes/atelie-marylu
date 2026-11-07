using System.Security.Claims;
using AtelieMarylu.Domain.Auditing;
using AtelieMarylu.Domain.Security;
using AtelieMarylu.Infrastructure.Data;
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
    AuditService audit)
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
            throw new AccessException("Você não pode remover o seu próprio acesso de Admin.");

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
        if (string.IsNullOrWhiteSpace(name)) throw new AccessException("Informe o nome do grupo.");
        if (AppRoles.IsSystemRole(name) || await roleManager.RoleExistsAsync(name))
            throw new AccessException("Já existe um grupo com esse nome.");
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
        await userManager.FindByIdAsync(userId) ?? throw new AccessException("Usuário não encontrado.");

    private async Task<IdentityRole> FindGroupAsync(string name)
    {
        if (AppRoles.IsSystemRole(name)) throw new AccessException("Papéis do sistema não podem ser alterados aqui.");
        return await roleManager.FindByNameAsync(name) ?? throw new AccessException("Grupo não encontrado.");
    }

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new AccessException(string.Join(" ", result.Errors.Select(e => e.Description)));
    }
}
