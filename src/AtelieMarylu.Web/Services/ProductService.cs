using AtelieMarylu.Domain.Payments;
using AtelieMarylu.Domain.Auditing;
using AtelieMarylu.Domain.Catalog;
using AtelieMarylu.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtelieMarylu.Web.Services;

// Consultas e gravação de produtos, para a vitrine e para o painel
public class ProductService(IDbContextFactory<ApplicationDbContext> dbFactory, AuditService audit)
{
    // Vitrine: só produtos ativos, com os esgotados no fim
    public async Task<List<Product>> ListStorefrontAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Products.AsNoTracking().Include(p => p.Images)
            .Where(p => p.IsActive)
            .OrderByDescending(p => p.Stock > 0).ThenBy(p => p.Name)
            .ToListAsync(ct);
    }

    // Painel: todos os produtos, inclusive os inativos
    public async Task<List<Product>> ListAllAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Products.AsNoTracking().Include(p => p.Images)
            .OrderByDescending(p => p.IsActive).ThenBy(p => p.Name)
            .ToListAsync(ct);
    }

    public async Task<List<Product>> ListActiveAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Products.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.Name).ToListAsync(ct);
    }

    public async Task<List<Product>> ListInStockAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Products.AsNoTracking().Where(p => p.IsActive && p.Stock > 0).OrderBy(p => p.Name).ToListAsync(ct);
    }

    // Produtos do carrinho, em um dicionário para achar cada um pelo Id
    public async Task<Dictionary<int, Product>> GetByIdsAsync(IEnumerable<int> ids, bool includeImages = false, CancellationToken ct = default)
    {
        var list = ids.Distinct().ToList();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.Products.AsNoTracking();
        if (includeImages) query = query.Include(p => p.Images);
        return await query.Where(p => list.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
    }

    public async Task<Product?> GetActiveAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Products.AsNoTracking().Include(p => p.Images).FirstOrDefaultAsync(p => p.Id == id && p.IsActive, ct);
    }

    public async Task<Product?> GetAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Products.AsNoTracking().Include(p => p.Images).FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    // Cria (id nulo) ou edita um produto e registra na auditoria.
    // Devolve o Id gravado, ou null se o produto a editar não existe mais.
    // Ao editar, o estoque recebe só o ajuste feito pelo admin: enquanto o formulário esteve aberto,
    // clientes podem ter comprado, e gravar o número da tela desfaria essas baixas.
    public async Task<int?> SaveAsync(int? id, ProductInput data, IReadOnlyList<SavedImage> photos, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        Product? product;
        if (id is null)
        {
            product = new Product { Stock = data.Stock };
            db.Products.Add(product);
        }
        else
        {
            product = await db.Products.Include(p => p.Images).FirstOrDefaultAsync(p => p.Id == id, ct);
            if (product is null) return null;
        }

        var stockBefore = product.Stock;
        product.Name = data.Name.Trim();
        product.Description = data.Description.Trim();
        product.Price = data.Price;
        product.IsActive = data.IsActive;
        product.ProductionDays = data.ProductionDays;
        product.WeightGrams = data.WeightGrams;
        product.HeightCm = data.HeightCm;
        product.WidthCm = data.WidthCm;
        product.LengthCm = data.LengthCm;

        product.Images.Clear();
        for (var i = 0; i < photos.Count; i++)
            product.Images.Add(new ProductImage { Url = photos[i].Url, ThumbnailUrl = photos[i].ThumbnailUrl, SortOrder = i });

        await SaveChangesCheckedAsync(db, ct);   // grava primeiro para o produto novo ganhar o Id

        var stockAfter = product.Stock;
        if (id is not null)
            stockAfter = await AdjustStockAsync(db, product.Id, data.Stock - data.StockWhenOpened, ct);

        await audit.RecordAsync(db, id is null ? AuditActions.ProductCreated : AuditActions.ProductEdited,
            AuditEntities.Product, product.Id, $"{product.Name} · {product.Price.ToBrl()} · estoque {stockBefore}→{stockAfter}");
        await SaveChangesCheckedAsync(db, ct);
        await transaction.CommitAsync(ct);

        return product.Id;
    }

    // UPDATE com condição no próprio banco, como na baixa de estoque dos pedidos
    private static async Task<int> AdjustStockAsync(ApplicationDbContext db, int productId, int adjustment, CancellationToken ct)
    {
        if (adjustment != 0)
        {
            var rows = await db.Products
                .Where(p => p.Id == productId && p.Stock + adjustment >= 0)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, p => p.Stock + adjustment), ct);
            if (rows == 0)
                throw new ProductRuleException("O estoque mudou enquanto você editava. Recarregue a página e confira o valor.");
        }
        return await db.Products.Where(p => p.Id == productId).Select(p => p.Stock).SingleAsync(ct);
    }

    private static async Task SaveChangesCheckedAsync(ApplicationDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ProductRuleException("O produto foi alterado por outra pessoa ao mesmo tempo. Recarregue a página e tente de novo.");
        }
    }
}
