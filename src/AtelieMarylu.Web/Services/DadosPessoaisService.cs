using AtelieMarylu.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AtelieMarylu.Web.Services;

// Direito de exclusão (LGPD, art. 18). Sem pedidos, a conta é apagada.
// Com pedidos, o registro da venda precisa ficar (obrigação fiscal), então a conta é anonimizada:
// some nome, e-mail, telefone, CPF, senha e acessos, e ninguém mais consegue entrar nela.
public class DadosPessoaisService(UserManager<ApplicationUser> userManager, IDbContextFactory<ApplicationDbContext> dbFactory, AuditoriaService auditoria)
{
    // LGPD: a pessoa tem direito a todos os seus dados, inclusive pedidos e endereços
    public async Task<IReadOnlyList<object>> ExportarPedidosAsync(string userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var pedidos = await db.Pedidos.AsNoTracking().Include(p => p.Itens)
            .Where(p => p.ClienteId == userId)
            .OrderBy(p => p.CriadoEm)
            .Select(p => new
            {
                p.Codigo, p.CriadoEm, Status = p.Status.ToString(), p.Endereco, p.FreteNome, p.FreteValor, p.CodigoRastreio,
                Itens = p.Itens.Select(i => new { i.NomeProduto, i.Quantidade, i.PrecoUnitario }),
            })
            .ToListAsync(ct);
        return pedidos;
    }

    public async Task<IdentityResult> ExcluirContaAsync(ApplicationUser usuario)
    {
        await auditoria.AnonimizarUsuarioAsync(usuario.Id, usuario.Email);

        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            if (!await db.Pedidos.AnyAsync(p => p.ClienteId == usuario.Id))
                return await userManager.DeleteAsync(usuario);
        }

        foreach (var login in await userManager.GetLoginsAsync(usuario))
            await userManager.RemoveLoginAsync(usuario, login.LoginProvider, login.ProviderKey);
        await userManager.RemoveClaimsAsync(usuario, await userManager.GetClaimsAsync(usuario));
        await userManager.RemoveFromRolesAsync(usuario, await userManager.GetRolesAsync(usuario));
        await userManager.RemovePasswordAsync(usuario);
        await userManager.SetTwoFactorEnabledAsync(usuario, false);

        var anonimo = $"removido-{usuario.Id}@anonimo.invalid";
        usuario.UserName = anonimo;
        usuario.Email = anonimo;
        usuario.EmailConfirmed = false;
        usuario.NomeCompleto = "Cliente removido";
        usuario.PhoneNumber = null;
        usuario.Cpf = null;
        usuario.LockoutEnabled = true;
        usuario.LockoutEnd = DateTimeOffset.MaxValue;

        var resultado = await userManager.UpdateAsync(usuario);
        if (resultado.Succeeded)
            await userManager.UpdateSecurityStampAsync(usuario);
        return resultado;
    }
}
