using System.Security.Claims;
using AtelieMarylu.Domain.Seguranca;
using AtelieMarylu.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AtelieMarylu.Web.Services;

public record ResumoUsuario(string Id, string Email, string Nome, bool EhAdmin, bool EhFuncionario, IReadOnlyList<string> Grupos);

public record AcessoUsuario(bool EhAdmin, IReadOnlyList<string> Grupos, IReadOnlyList<string> PermissoesIndividuais);

public record ResumoGrupo(string Nome, IReadOnlyList<string> Permissoes, int Membros);

public class AcessoException(string mensagem) : Exception(mensagem);

// Administração de acesso (só o Admin usa):
// - grupo de funcionários = Role do Identity com claims "permissao"
// - permissão individual = claim "permissao" no próprio usuário
// Ao mudar o acesso de alguém, o security stamp é renovado para o login dele ser reavaliado.
public class AcessoService(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IDbContextFactory<ApplicationDbContext> dbFactory,
    AuditoriaService auditoria)
{
    public async Task<IReadOnlyList<ResumoUsuario>> ListarUsuariosAsync(string? busca)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var consulta = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(busca))
        {
            var termo = $"%{busca.Trim()}%";
            consulta = consulta.Where(u => EF.Functions.ILike(u.Email!, termo) || EF.Functions.ILike(u.NomeCompleto, termo));
        }
        var usuarios = await consulta.OrderBy(u => u.NomeCompleto).Take(200).ToListAsync();
        var ids = usuarios.Select(u => u.Id).ToList();

        var papeis = await (from ur in db.UserRoles
                            join r in db.Roles on ur.RoleId equals r.Id
                            where ids.Contains(ur.UserId)
                            select new { ur.UserId, r.Name }).ToListAsync();
        var comPermissao = await db.UserClaims
            .Where(c => ids.Contains(c.UserId) && c.ClaimType == Permissoes.TipoClaim)
            .Select(c => c.UserId).Distinct().ToListAsync();

        return usuarios.Select(u =>
        {
            var nomes = papeis.Where(p => p.UserId == u.Id).Select(p => p.Name!).ToList();
            var grupos = nomes.Where(n => !Papeis.EhPapelDoSistema(n)).ToList();
            return new ResumoUsuario(u.Id, u.Email ?? "", u.NomeCompleto, nomes.Contains(Papeis.Admin),
                grupos.Count > 0 || comPermissao.Contains(u.Id), grupos);
        }).ToList();
    }

    public async Task<AcessoUsuario> ObterAcessoAsync(string userId)
    {
        var usuario = await BuscarAsync(userId);
        var papeis = await userManager.GetRolesAsync(usuario);
        var claims = await userManager.GetClaimsAsync(usuario);
        return new AcessoUsuario(
            papeis.Contains(Papeis.Admin),
            papeis.Where(p => !Papeis.EhPapelDoSistema(p)).ToList(),
            claims.Where(c => c.Type == Permissoes.TipoClaim).Select(c => c.Value).ToList());
    }

    public async Task SalvarAcessoAsync(string userId, string idAdminLogado, AcessoUsuario acesso)
    {
        var usuario = await BuscarAsync(userId);
        var papeisAtuais = await userManager.GetRolesAsync(usuario);

        if (userId == idAdminLogado && !acesso.EhAdmin)
            throw new AcessoException("Você não pode remover o seu próprio acesso de Admin.");

        // Papéis desejados: Admin (se marcado) + grupos. "Cliente" nunca é mexido aqui.
        var gruposValidos = roleManager.Roles.Select(r => r.Name!).ToList().Where(n => !Papeis.EhPapelDoSistema(n)).ToHashSet();
        var desejados = acesso.Grupos.Where(gruposValidos.Contains).ToHashSet();
        if (acesso.EhAdmin) desejados.Add(Papeis.Admin);

        var gerenciaveis = papeisAtuais.Where(p => p != Papeis.Cliente).ToList();
        Verificar(await userManager.RemoveFromRolesAsync(usuario, gerenciaveis.Except(desejados)));
        Verificar(await userManager.AddToRolesAsync(usuario, desejados.Except(gerenciaveis)));

        var claimsAtuais = (await userManager.GetClaimsAsync(usuario)).Where(c => c.Type == Permissoes.TipoClaim).ToList();
        var permissoesDesejadas = acesso.PermissoesIndividuais.Where(Permissoes.Todas.ContainsKey).ToHashSet();
        Verificar(await userManager.RemoveClaimsAsync(usuario, claimsAtuais.Where(c => !permissoesDesejadas.Contains(c.Value))));
        Verificar(await userManager.AddClaimsAsync(usuario, permissoesDesejadas
            .Where(p => claimsAtuais.All(c => c.Value != p))
            .Select(p => new Claim(Permissoes.TipoClaim, p))));

        await userManager.UpdateSecurityStampAsync(usuario);
        await RegistrarAsync("acesso.alterado", "Usuario", usuario.Email ?? userId,
            $"Admin: {(acesso.EhAdmin ? "sim" : "não")} · Grupos: {string.Join(", ", desejados.Where(d => d != Papeis.Admin))} · Individuais: {string.Join(", ", permissoesDesejadas)}");
    }

    public async Task<IReadOnlyList<ResumoGrupo>> ListarGruposAsync()
    {
        var grupos = new List<ResumoGrupo>();
        foreach (var role in roleManager.Roles.ToList().Where(r => !Papeis.EhPapelDoSistema(r.Name!)).OrderBy(r => r.Name))
        {
            var claims = await roleManager.GetClaimsAsync(role);
            var membros = await userManager.GetUsersInRoleAsync(role.Name!);
            grupos.Add(new ResumoGrupo(role.Name!, claims.Where(c => c.Type == Permissoes.TipoClaim).Select(c => c.Value).ToList(), membros.Count));
        }
        return grupos;
    }

    public async Task CriarGrupoAsync(string nome)
    {
        nome = nome.Trim();
        if (string.IsNullOrWhiteSpace(nome)) throw new AcessoException("Informe o nome do grupo.");
        if (Papeis.EhPapelDoSistema(nome) || await roleManager.RoleExistsAsync(nome))
            throw new AcessoException("Já existe um grupo com esse nome.");
        Verificar(await roleManager.CreateAsync(new IdentityRole(nome)));
        await RegistrarAsync("grupo.criado", "Grupo", nome);
    }

    public async Task SalvarPermissoesGrupoAsync(string nome, IEnumerable<string> permissoes)
    {
        var role = await BuscarGrupoAsync(nome);
        var atuais = (await roleManager.GetClaimsAsync(role)).Where(c => c.Type == Permissoes.TipoClaim).ToList();
        var desejadas = permissoes.Where(Permissoes.Todas.ContainsKey).ToHashSet();

        foreach (var claim in atuais.Where(c => !desejadas.Contains(c.Value)))
            Verificar(await roleManager.RemoveClaimAsync(role, claim));
        foreach (var p in desejadas.Where(p => atuais.All(c => c.Value != p)))
            Verificar(await roleManager.AddClaimAsync(role, new Claim(Permissoes.TipoClaim, p)));

        await RenovarMembrosAsync(nome);
        await RegistrarAsync("grupo.permissoes", "Grupo", nome, string.Join(", ", desejadas));
    }

    public async Task ExcluirGrupoAsync(string nome)
    {
        var role = await BuscarGrupoAsync(nome);
        var membros = await userManager.GetUsersInRoleAsync(nome);
        Verificar(await roleManager.DeleteAsync(role));
        foreach (var membro in membros)
            await userManager.UpdateSecurityStampAsync(membro);
        await RegistrarAsync("grupo.excluido", "Grupo", nome, $"{membros.Count} membro(s)");
    }

    private async Task RegistrarAsync(string acao, string entidade, string id, string? detalhes = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await auditoria.RegistrarAsync(db, acao, entidade, id, detalhes);
        await db.SaveChangesAsync();
    }

    private async Task RenovarMembrosAsync(string grupo)
    {
        foreach (var membro in await userManager.GetUsersInRoleAsync(grupo))
            await userManager.UpdateSecurityStampAsync(membro);
    }

    private async Task<ApplicationUser> BuscarAsync(string userId) =>
        await userManager.FindByIdAsync(userId) ?? throw new AcessoException("Usuário não encontrado.");

    private async Task<IdentityRole> BuscarGrupoAsync(string nome)
    {
        if (Papeis.EhPapelDoSistema(nome)) throw new AcessoException("Papéis do sistema não podem ser alterados aqui.");
        return await roleManager.FindByNameAsync(nome) ?? throw new AcessoException("Grupo não encontrado.");
    }

    private static void Verificar(IdentityResult resultado)
    {
        if (!resultado.Succeeded)
            throw new AcessoException(string.Join(" ", resultado.Errors.Select(e => e.Description)));
    }
}
