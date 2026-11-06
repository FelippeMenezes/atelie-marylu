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
    public async Task<Dictionary<int, Produto>> ObterPorIdsAsync(IEnumerable<int> ids, bool incluirImagens = false, CancellationToken ct = default)
    {
        var lista = ids.Distinct().ToList();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var consulta = db.Produtos.AsNoTracking();
        if (incluirImagens) consulta = consulta.Include(p => p.Imagens);
        return await consulta.Where(p => lista.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
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
    // Ao editar, o estoque recebe só o ajuste feito pelo admin: enquanto o formulário esteve aberto,
    // clientes podem ter comprado, e gravar o número da tela desfaria essas baixas.
    public async Task<int?> SalvarAsync(int? id, DadosProduto dados, IReadOnlyList<ImagemSalva> fotos, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transacao = await db.Database.BeginTransactionAsync(ct);

        Produto? produto;
        if (id is null)
        {
            produto = new Produto { Estoque = dados.Estoque };
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
        produto.Ativo = dados.Ativo;
        produto.PrazoProducaoDias = dados.PrazoProducaoDias;
        produto.PesoGramas = dados.PesoGramas;
        produto.AlturaCm = dados.AlturaCm;
        produto.LarguraCm = dados.LarguraCm;
        produto.ComprimentoCm = dados.ComprimentoCm;

        produto.Imagens.Clear();
        for (var i = 0; i < fotos.Count; i++)
            produto.Imagens.Add(new ProdutoImagem { Url = fotos[i].Url, UrlMiniatura = fotos[i].UrlMiniatura, Ordem = i });

        await SalvarAlteracoesAsync(db, ct);   // grava primeiro para o produto novo ganhar o Id

        var estoqueDepois = produto.Estoque;
        if (id is not null)
            estoqueDepois = await AjustarEstoqueAsync(db, produto.Id, dados.Estoque - dados.EstoqueAoAbrir, ct);

        await auditoria.RegistrarAsync(db, id is null ? AcoesAuditoria.ProdutoCriado : AcoesAuditoria.ProdutoEditado,
            EntidadesAuditoria.Produto, produto.Id, $"{produto.Nome} · {produto.Preco:C} · estoque {estoqueAntes}→{estoqueDepois}");
        await SalvarAlteracoesAsync(db, ct);
        await transacao.CommitAsync(ct);

        return produto.Id;
    }

    // UPDATE com condição no próprio banco, como na baixa de estoque dos pedidos
    private static async Task<int> AjustarEstoqueAsync(ApplicationDbContext db, int produtoId, int ajuste, CancellationToken ct)
    {
        if (ajuste != 0)
        {
            var linhas = await db.Produtos
                .Where(p => p.Id == produtoId && p.Estoque + ajuste >= 0)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Estoque, p => p.Estoque + ajuste), ct);
            if (linhas == 0)
                throw new RegraDeProdutoException("O estoque mudou enquanto você editava. Recarregue a página e confira o valor.");
        }
        return await db.Produtos.Where(p => p.Id == produtoId).Select(p => p.Estoque).SingleAsync(ct);
    }

    private static async Task SalvarAlteracoesAsync(ApplicationDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new RegraDeProdutoException("O produto foi alterado por outra pessoa ao mesmo tempo. Recarregue a página e tente de novo.");
        }
    }
}
