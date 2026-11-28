using AtelieMarylu.Domain.Payments;
using AtelieMarylu.Domain.Auditing;
using AtelieMarylu.Domain.Catalog;
using AtelieMarylu.Domain.Orders;
using AtelieMarylu.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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

    // Estoque à venda agora: 0 se o produto esgotou ou saiu da loja. A página de produto é estática e
    // mostra o estoque de quando abriu; o "adicionar ao carrinho" confere de novo na hora do clique.
    public async Task<int> GetStockForSaleAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Products.AsNoTracking()
            .Where(p => p.Id == id && p.IsActive)
            .Select(p => p.Stock)
            .FirstOrDefaultAsync(ct);
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

    // Exclui o produto de vez (linha, fotos no banco e registro na auditoria).
    // Devolve as fotos para a tela apagar os arquivos depois do commit, ou null se o produto já não existe.
    // Produto que já foi vendido não sai: o pedido precisa continuar apontando para ele (FK Restrict em OrderItems).
    public async Task<IReadOnlyList<SavedImage>?> DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var product = await db.Products.Include(p => p.Images).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null) return null;

        // Regra: produto vendido fica, porque o pedido precisa dele (histórico fiscal de 5 anos).
        // A conferência deixa a regra explícita aqui e evita usar a exceção do banco como caminho normal;
        // a FK Restrict de OrderItems continua sendo a garantia final (veja o catch abaixo).
        // AnyAsync vira um EXISTS no Postgres: para no primeiro item, sem contar todos.
        if (await db.Set<OrderItem>().AnyAsync(i => i.ProductId == id, ct))
            throw new ProductRuleException(SoldProductMessage);

        var photos = product.Images.Select(i => new SavedImage(i.Url, i.ThumbnailUrl)).ToList();
        db.Products.Remove(product);   // as ProductImages caem em cascata
        await audit.RecordAsync(db, AuditActions.ProductDeleted, AuditEntities.Product, product.Id,
            $"{product.Name} · {product.Price.ToBrl()} · estoque {product.Stock}");

        try
        {
            await db.SaveChangesAsync(ct);
        }
        // Outra exclusão do mesmo produto (duplo clique, outra aba, outro admin) gravou primeiro: o DELETE
        // afetou 0 linhas. O resultado que a pessoa pediu já aconteceu, então a exclusão é idempotente:
        // devolve "já não existe", sem segundo registro na auditoria (ele sai junto com o rollback).
        catch (DbUpdateConcurrencyException)
        {
            return null;
        }
        // Corrida: um pedido com este produto foi criado entre a conferência acima e o DELETE.
        // A FK do banco é a garantia final; aqui só trocamos o erro técnico pela mesma mensagem da regra.
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            throw new ProductRuleException(SoldProductMessage);
        }
        await transaction.CommitAsync(ct);

        return photos;
    }

    // Usada pela regra e pela corrida no banco, para a loja ver sempre o mesmo texto
    public const string SoldProductMessage = "This product is in orders and cannot be deleted. Deactivate it to hide it from the store.";

    // UPDATE com condição no próprio banco, como na baixa de estoque dos pedidos
    private static async Task<int> AdjustStockAsync(ApplicationDbContext db, int productId, int adjustment, CancellationToken ct)
    {
        if (adjustment != 0)
        {
            var rows = await db.Products
                .Where(p => p.Id == productId && p.Stock + adjustment >= 0)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, p => p.Stock + adjustment), ct);
            if (rows == 0)
                throw new ProductRuleException("The stock changed while you were editing. Reload the page and check the value.");
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
            throw new ProductRuleException("Someone else changed the product at the same time. Reload the page and try again.");
        }
    }
}
