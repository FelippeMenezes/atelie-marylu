using AtelieMarylu.Domain.Auditing;
using AtelieMarylu.Domain.Catalog;
using AtelieMarylu.Domain.Security;
using AtelieMarylu.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AtelieMarylu.Tests.Integration;

// Os serviços que substituíram as consultas feitas dentro das telas
public class QueryServiceTests(TestDatabase database) : IClassFixture<TestDatabase>
{
    private AuditService AuditEntries() =>
        new(new CurrentUserService(new ServiceCollection().BuildServiceProvider(), new HttpContextAccessor()), database);

    private ProductService Products() => new(database, AuditEntries());

    // Mesma operação da baixa de estoque de um pedido
    private async Task SellAsync(int productId, int quantity)
    {
        await using var db = database.CreateDbContext();
        await db.Products.Where(p => p.Id == productId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, p => p.Stock - quantity));
    }

    private async Task<string> CreateCustomerWithRoleAsync(string name)
    {
        var customer = await database.CreateCustomerAsync();
        await using var db = database.CreateDbContext();
        var role = await db.Roles.FirstOrDefaultAsync(role => role.Name == AppRoles.Customer);
        if (role is null)
        {
            role = new IdentityRole(AppRoles.Customer) { NormalizedName = AppRoles.Customer.ToUpperInvariant() };
            db.Roles.Add(role);
        }
        db.UserRoles.Add(new IdentityUserRole<string> { RoleId = role.Id, UserId = customer.Id });
        var user = await db.Users.SingleAsync(u => u.Id == customer.Id);
        user.FullName = name;
        await db.SaveChangesAsync();
        return customer.Id;
    }

    private static ProductInput BuildInput(string name = "Bolsa", int stock = 3, bool active = true, int stockWhenOpened = 0) =>
        new(name, "Descrição", 80m, stock, active, null, 300, 5, 20, 25, stockWhenOpened);

    [FactWithDatabase]
    public async Task Save_creates_product_with_photos_and_audits()
    {
        var id = await Products().SaveAsync(null, BuildInput(), [new SavedImage("/a.webp", "/a-mini.webp")]);

        var product = await Products().GetAsync(id!.Value);
        Assert.Equal("Bolsa", product!.Name);
        Assert.Single(product.Images);

        await using var db = database.CreateDbContext();
        Assert.True(await db.AuditEntries.AnyAsync(a => a.Action == AuditActions.ProductCreated && a.EntityId == id.Value.ToString()));
    }

    [FactWithDatabase]
    public async Task Save_edits_product_and_replaces_photos()
    {
        var id = await Products().SaveAsync(null, BuildInput(), [new SavedImage("/a.webp", "/a-mini.webp")]);

        await Products().SaveAsync(id, BuildInput("Bolsa nova", stock: 9, stockWhenOpened: 3), [new SavedImage("/b.webp", "/b-mini.webp")]);

        var product = await Products().GetAsync(id!.Value);
        Assert.Equal("Bolsa nova", product!.Name);
        Assert.Equal(9, product.Stock);
        Assert.Equal("/b.webp", Assert.Single(product.Images).Url);
    }

    [FactWithDatabase]
    public async Task Saving_missing_product_returns_null()
    {
        Assert.Null(await Products().SaveAsync(int.MaxValue, BuildInput(), []));
    }

    // Formulário aberto com 5, cliente compra 2 e o admin salva só uma mudança de nome
    [FactWithDatabase]
    public async Task Save_does_not_undo_sale_made_while_form_was_open()
    {
        var id = (await Products().SaveAsync(null, BuildInput(stock: 5), []))!.Value;
        await SellAsync(id, 2);

        await Products().SaveAsync(id, BuildInput("Outro nome", stock: 5, stockWhenOpened: 5), []);

        Assert.Equal(3, await database.GetStockAsync(id));
    }

    [FactWithDatabase]
    public async Task Save_applies_only_admin_adjustment_to_current_stock()
    {
        var id = (await Products().SaveAsync(null, BuildInput(stock: 5), []))!.Value;
        await SellAsync(id, 2);

        await Products().SaveAsync(id, BuildInput(stock: 8, stockWhenOpened: 5), []);

        Assert.Equal(6, await database.GetStockAsync(id));
    }

    [FactWithDatabase]
    public async Task Save_rejects_adjustment_that_would_make_stock_negative()
    {
        var id = (await Products().SaveAsync(null, BuildInput(stock: 5), []))!.Value;
        await SellAsync(id, 4);

        await Assert.ThrowsAsync<ProductRuleException>(() =>
            Products().SaveAsync(id, BuildInput(stock: 2, stockWhenOpened: 5), []));

        Assert.Equal(1, await database.GetStockAsync(id));
    }

    [FactWithDatabase]
    public async Task Customer_search_treats_wildcards_as_text()
    {
        var target = await CreateCustomerWithRoleAsync("maria_souza");
        var similar = await CreateCustomerWithRoleAsync("mariaXsouza");

        var found = await new CustomerService(database).ListAsync("maria_souza");

        Assert.Contains(found, c => c.Id == target);
        Assert.DoesNotContain(found, c => c.Id == similar);
    }

    [FactWithDatabase]
    public async Task Anonymize_removes_email_from_history()
    {
        var userId = Guid.NewGuid().ToString();
        var email = $"{userId}@teste.local";
        await using (var db = database.CreateDbContext())
        {
            await AuditEntries().RecordAsync(db, AuditActions.OrderCreated, AuditEntities.Order, 1, null, new Author(userId, email));
            await AuditEntries().RecordAsync(db, AuditActions.AccessChanged, AuditEntities.User, email, null, Author.Automated);
            await db.SaveChangesAsync();
        }

        await AuditEntries().AnonymizeUserAsync(userId, email);

        await using var reading = database.CreateDbContext();
        Assert.False(await reading.AuditEntries.AnyAsync(a => a.Author == email || a.EntityId == email));
        Assert.Equal(2, await reading.AuditEntries.CountAsync(a => a.UserId == userId || a.EntityId == userId));
    }

    [FactWithDatabase]
    public async Task Storefront_does_not_show_inactive_product()
    {
        var inactive = await Products().SaveAsync(null, BuildInput("Oculto", active: false), []);

        Assert.DoesNotContain(await Products().ListStorefrontAsync(), p => p.Id == inactive);
        Assert.Null(await Products().GetActiveAsync(inactive!.Value));
    }

    [FactWithDatabase]
    public async Task Customer_list_only_includes_customer_role()
    {
        var customer = await database.CreateCustomerAsync();
        var withoutRole = await database.CreateCustomerAsync();
        await using (var db = database.CreateDbContext())
        {
            var role = await db.Roles.FirstOrDefaultAsync(role => role.Name == AppRoles.Customer);
            if (role is null)
            {
                role = new IdentityRole(AppRoles.Customer) { NormalizedName = AppRoles.Customer.ToUpperInvariant() };
                db.Roles.Add(role);
            }
            db.UserRoles.Add(new IdentityUserRole<string> { RoleId = role.Id, UserId = customer.Id });
            await db.SaveChangesAsync();
        }

        var customers = await new CustomerService(database).ListForSelectionAsync();

        Assert.Contains(customers, c => c.Id == customer.Id);
        Assert.DoesNotContain(customers, c => c.Id == withoutRole.Id);
    }
}
