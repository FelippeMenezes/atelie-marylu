using AtelieMarylu.Domain.Auditing;
using AtelieMarylu.Domain.Catalog;
using AtelieMarylu.Domain.Orders;
using AtelieMarylu.Domain.Shipping;
using AtelieMarylu.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AtelieMarylu.Tests.Integration;

// Exclusão de produto pelo painel (ProductService.DeleteAsync): sem pedidos, sai de vez;
// com pedidos, fica, porque o pedido precisa continuar apontando para ele.
public class ProductDeletionTests(TestDatabase database) : IClassFixture<TestDatabase>
{
    private ProductService Products() =>
        new(database, new AuditService(new CurrentUserService(new ServiceCollection().BuildServiceProvider(), new HttpContextAccessor()), database));

    private static ProductInput BuildInput() => new("Bolsa de tecido", "Descrição", 80m, 3, true, null, 300, 5, 20, 25, 0);

    [FactWithDatabase]
    public async Task Delete_removes_product_without_orders_with_photos_and_audits()
    {
        var id = (await Products().SaveAsync(null, BuildInput(), [new SavedImage("/uploads/produtos/a.webp", "/uploads/produtos/a-mini.webp")]))!.Value;

        var photos = await Products().DeleteAsync(id);

        Assert.Equal([new SavedImage("/uploads/produtos/a.webp", "/uploads/produtos/a-mini.webp")], photos);
        await using var db = database.CreateDbContext();
        Assert.False(await db.Products.AnyAsync(p => p.Id == id));
        Assert.False(await db.Set<ProductImage>().AnyAsync(i => i.ProductId == id));
        var entry = await db.AuditEntries.SingleAsync(a => a.Action == AuditActions.ProductDeleted && a.EntityId == id.ToString());
        Assert.Contains("Bolsa de tecido", entry.Details);
    }

    [FactWithDatabase]
    public async Task Delete_refuses_product_that_is_in_an_order()
    {
        var product = await database.CreateProductAsync(stock: 5);
        await CreateOrderAsync(product);

        var ex = await Assert.ThrowsAsync<ProductRuleException>(() => Products().DeleteAsync(product.Id));

        Assert.Equal(ProductService.SoldProductMessage, ex.Message);
        await using var db = database.CreateDbContext();
        Assert.True(await db.Products.AnyAsync(p => p.Id == product.Id));
        Assert.False(await db.AuditEntries.AnyAsync(a => a.Action == AuditActions.ProductDeleted && a.EntityId == product.Id.ToString()));
    }

    // A conferência do serviço não cobre a corrida (pedido criado entre a conferência e o DELETE):
    // quem garante é a FK. Se ela virar Cascade, apagar um produto apagaria os itens dos pedidos.
    [FactWithDatabase]
    public async Task Order_items_keep_restricting_product_deletion_in_the_database()
    {
        await using var db = database.CreateDbContext();
        var foreignKey = db.Model.FindEntityType(typeof(OrderItem))!.GetForeignKeys()
            .Single(fk => fk.PrincipalEntityType.ClrType == typeof(Product));

        Assert.Equal(Microsoft.EntityFrameworkCore.DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
    }

    // Duplo clique, duas abas ou dois admins: antes da correção, a segunda exclusão estourava
    // DbUpdateConcurrencyException (o DELETE afetava 0 linhas) e derrubava a tela.
    // Agora exatamente uma apaga e audita, e a outra responde "já não existe".
    [FactWithDatabase]
    public async Task Concurrent_deletes_of_the_same_product_are_idempotent()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var product = await database.CreateProductAsync(stock: 1);

            var results = await Task.WhenAll(Products().DeleteAsync(product.Id), Products().DeleteAsync(product.Id));

            Assert.Single(results, r => r is not null);
            await using var db = database.CreateDbContext();
            Assert.False(await db.Products.AnyAsync(p => p.Id == product.Id));
            Assert.Equal(1, await db.AuditEntries.CountAsync(a => a.Action == AuditActions.ProductDeleted && a.EntityId == product.Id.ToString()));
        }
    }

    [FactWithDatabase]
    public async Task Delete_returns_null_when_product_no_longer_exists()
    {
        Assert.Null(await Products().DeleteAsync(int.MaxValue));
    }

    private async Task CreateOrderAsync(Product product)
    {
        var customer = await database.CreateCustomerAsync();
        await using var db = database.CreateDbContext();
        db.Orders.Add(new Order(customer.Id, [new OrderItem(product.Id, product.Name, product.Price, 1)],
            new ShippingAddress { PostalCode = "20000000", Street = "Rua A", Number = "1", District = "Centro", City = "Rio de Janeiro", State = "RJ" },
            new ShippingOption("pac", "PAC", 10m, 8), DateTime.UtcNow.AddDays(1)));
        await db.SaveChangesAsync();
    }
}
