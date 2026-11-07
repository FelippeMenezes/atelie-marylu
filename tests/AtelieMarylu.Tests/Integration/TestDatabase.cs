using AtelieMarylu.Domain.Catalog;
using AtelieMarylu.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace AtelieMarylu.Tests.Integration;

// Os testes de integração usam um Postgres de verdade. Defina a variável de ambiente
// ATELIE_TESTE_DB com uma connection string (sem Database); cada execução cria um banco
// temporário com as migrations e o apaga no fim. Sem a variável, os testes são pulados.
public sealed class FactWithDatabaseAttribute : FactAttribute
{
    public FactWithDatabaseAttribute()
    {
        if (string.IsNullOrWhiteSpace(TestDatabase.BaseConnection))
            Skip = "Defina ATELIE_TESTE_DB para rodar os testes de integração.";
    }
}

public sealed class TestDatabase : IAsyncLifetime, IDbContextFactory<ApplicationDbContext>
{
    public static string? BaseConnection => Environment.GetEnvironmentVariable("ATELIE_TESTE_DB");

    private readonly string _name = $"atelie_teste_{Guid.NewGuid():N}";
    private ServiceProvider? _services;

    public ApplicationDbContext CreateDbContext() =>
        _services!.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext();

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(BaseConnection)) return;
        var connection = new NpgsqlConnectionStringBuilder(BaseConnection) { Database = _name }.ConnectionString;
        // Mesma configuração do Program.cs: o modelo do Identity depende da SchemaVersion
        var services = new ServiceCollection();
        services.AddDbContextFactory<ApplicationDbContext>(o => o.UseNpgsql(connection));
        services.AddIdentityCore<ApplicationUser>(o => o.Stores.SchemaVersion = IdentitySchemaVersions.Version2)
            .AddEntityFrameworkStores<ApplicationDbContext>();
        _services = services.BuildServiceProvider();
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrWhiteSpace(BaseConnection)) return;
        await using var db = CreateDbContext();
        await db.Database.EnsureDeletedAsync();
        await _services!.DisposeAsync();
    }

    public async Task<ApplicationUser> CreateCustomerAsync()
    {
        await using var db = CreateDbContext();
        var id = Guid.NewGuid().ToString();
        var user = new ApplicationUser { Id = id, UserName = $"{id}@teste.local", Email = $"{id}@teste.local", FullName = "Cliente Teste" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    public async Task<Product> CreateProductAsync(int stock, decimal price = 50m)
    {
        await using var db = CreateDbContext();
        var product = new Product { Name = "Bolsa teste", Price = price, Stock = stock };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product;
    }

    public async Task<int> GetStockAsync(int productId)
    {
        await using var db = CreateDbContext();
        return await db.Products.Where(p => p.Id == productId).Select(p => p.Stock).SingleAsync();
    }
}
