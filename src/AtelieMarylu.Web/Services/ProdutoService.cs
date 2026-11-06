using AtelieMarylu.Domain.Auditoria;
using AtelieMarylu.Domain.Catalogo;
using AtelieMarylu.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtelieMarylu.Web.Services;

// Consultas e gravação de produtos, para a vitrine e para o painel
public class ProdutoService(IDbContextFactory<ApplicationDbContext> dbFactory, AuditoriaService auditoria)
{
    // Vitrine: só produtos ativos, com os esgotados no fim
    public async Task<List<Produto>> ListarVitrineAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Produtos.AsNoTracking().Include(p => p.Imagens)
            .Where(p => p.Ativo)
            .OrderByDescending(p => p.Estoque > 0).ThenBy(p => p.Nome)
            .ToListAsync(ct);
    }

    // Painel: todos os produtos, inclusive os inativos
    public async Task<List<Produto>> ListarTodosAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Produtos.AsNoTracking().Include(p => p.Imagens)
            .OrderByDescending(p => p.Ativo).ThenBy(p => p.Nome)
            .ToListAsync(ct);
    }

    public async Task<List<Produto>> ListarAtivosAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Produtos.AsNoTracking().Where(p => p.Ativo).OrderBy(p => p.Nome).ToListAsync(ct);
    }

    public async Task<List<Produto>> ListarComEstoqueAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Produtos.AsNoTracking().Where(p => p.Ativo && p.Estoque > 0).OrderBy(p => p.Nome).ToListAsync(ct);
    }

    // Produtos do carrinho, em um dicionário para achar cada um pelo Id
    public async Task<Dictionary<int, Produto>> ObterPorIdsAsync(IEnumerable<int> ids, CancellationToken ct = default)
    {
        var lista = ids.Distinct().ToList();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Produtos.AsNoTracking().Include(p => p.Imagens)
            .Where(p => lista.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);
    }

    public async Task<Produto?> ObterAtivoAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Produtos.AsNoTracking().Include(p => p.Imagens).FirstOrDefaultAsync(p => p.Id == id && p.Ativo, ct);
    }

    public async Task<Produto?> ObterAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Produtos.AsNoTracking().Include(p => p.Imagens).FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    // Cria (id nulo) ou edita um produto e registra na auditoria.
    // Devolve o Id gravado, ou null se o produto a editar não existe mais.
    public async Task<int?> SalvarAsync(int? id, DadosProduto dados, IReadOnlyList<ImagemSalva> fotos, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transacao = await db.Database.BeginTransactionAsync(ct);

        Produto? produto;
        if (id is null)
        {
            produto = new Produto();
            db.Produtos.Add(produto);
        }
        else
        {
            produto = await db.Produtos.Include(p => p.Imagens).FirstOrDefaultAsync(p => p.Id == id, ct);
            if (produto is null) return null;
        }

        var estoqueAntes = produto.Estoque;
        produto.Nome = dados.Nome.Trim();
        produto.Descricao = dados.Descricao.Trim();
        produto.Preco = dados.Preco;
        produto.Estoque = dados.Estoque;
        produto.Ativo = dados.Ativo;
        produto.PrazoProducaoDias = dados.PrazoProducaoDias;
        produto.PesoGramas = dados.PesoGramas;
        produto.AlturaCm = dados.AlturaCm;
        produto.LarguraCm = dados.LarguraCm;
        produto.ComprimentoCm = dados.ComprimentoCm;

        produto.Imagens.Clear();
        for (var i = 0; i < fotos.Count; i++)
            produto.Imagens.Add(new ProdutoImagem { Url = fotos[i].Url, UrlMiniatura = fotos[i].UrlMiniatura, Ordem = i });

        await db.SaveChangesAsync(ct);   // grava primeiro para o produto novo ganhar o Id
        await auditoria.RegistrarAsync(db, id is null ? AcoesAuditoria.ProdutoCriado : AcoesAuditoria.ProdutoEditado,
            EntidadesAuditoria.Produto, produto.Id, $"{produto.Nome} · {produto.Preco:C} · estoque {estoqueAntes}→{produto.Estoque}");
        await db.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);

        return produto.Id;
    }
}
