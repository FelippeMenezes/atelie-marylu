using AtelieMarylu.Domain.Catalogo;
using AtelieMarylu.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace AtelieMarylu.Tests.Integracao;

// Os testes de integração usam um Postgres de verdade. Defina a variável de ambiente
// ATELIE_TESTE_DB com uma connection string (sem Database); cada execução cria um banco
// temporário com as migrations e o apaga no fim. Sem a variável, os testes são pulados.
public sealed class FactComBancoAttribute : FactAttribute
{
    public FactComBancoAttribute()
    {
        if (string.IsNullOrWhiteSpace(BancoDeTeste.ConexaoBase))
            Skip = "Defina ATELIE_TESTE_DB para rodar os testes de integração.";
    }
}

public sealed class BancoDeTeste : IAsyncLifetime, IDbContextFactory<ApplicationDbContext>
{
    public static string? ConexaoBase => Environment.GetEnvironmentVariable("ATELIE_TESTE_DB");

    private readonly string _nome = $"atelie_teste_{Guid.NewGuid():N}";
    private ServiceProvider? _servicos;

    public ApplicationDbContext CreateDbContext() =>
        _servicos!.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext();

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(ConexaoBase)) return;
        var conexao = new NpgsqlConnectionStringBuilder(ConexaoBase) { Database = _nome }.ConnectionString;
        // Mesma configuração do Program.cs: o modelo do Identity depende da SchemaVersion
        var servicos = new ServiceCollection();
        servicos.AddDbContextFactory<ApplicationDbContext>(o => o.UseNpgsql(conexao));
        servicos.AddIdentityCore<ApplicationUser>(o => o.Stores.SchemaVersion = IdentitySchemaVersions.Version3)
            .AddEntityFrameworkStores<ApplicationDbContext>();
        _servicos = servicos.BuildServiceProvider();
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrWhiteSpace(ConexaoBase)) return;
        await using var db = CreateDbContext();
        await db.Database.EnsureDeletedAsync();
        await _servicos!.DisposeAsync();
    }

    public async Task<ApplicationUser> CriarClienteAsync()
    {
        await using var db = CreateDbContext();
        var id = Guid.NewGuid().ToString();
        var usuario = new ApplicationUser { Id = id, UserName = $"{id}@teste.local", Email = $"{id}@teste.local", NomeCompleto = "Cliente Teste" };
        db.Users.Add(usuario);
        await db.SaveChangesAsync();
        return usuario;
    }

    public async Task<Produto> CriarProdutoAsync(int estoque, decimal preco = 50m)
    {
        await using var db = CreateDbContext();
        var produto = new Produto { Nome = "Bolsa teste", Preco = preco, Estoque = estoque };
        db.Produtos.Add(produto);
        await db.SaveChangesAsync();
        return produto;
    }

    public async Task<int> EstoqueAsync(int produtoId)
    {
        await using var db = CreateDbContext();
        return await db.Produtos.Where(p => p.Id == produtoId).Select(p => p.Estoque).SingleAsync();
    }
}
