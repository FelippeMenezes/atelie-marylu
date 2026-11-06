using AtelieMarylu.Domain.Auditoria;
using AtelieMarylu.Domain.Catalogo;
using AtelieMarylu.Domain.Pagamentos;
using AtelieMarylu.Domain.Pedidos;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AtelieMarylu.Infrastructure.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options), IDataProtectionKeyContext
{
    public DbSet<Produto> Produtos => Set<Produto>();
    public DbSet<ProdutoImagem> ProdutoImagens => Set<ProdutoImagem>();
    public DbSet<Pedido> Pedidos => Set<Pedido>();
    public DbSet<Pagamento> Pagamentos => Set<Pagamento>();
    public DbSet<NotificacaoProcessada> NotificacoesProcessadas => Set<NotificacaoProcessada>();
    public DbSet<RegistroAuditoria> Auditoria => Set<RegistroAuditoria>();

    // Chaves que criptografam cookies de login e o carrinho; no banco, sobrevivem a reinícios e deploys
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Lê as classes da pasta Configurations: cada tabela tem o seu próprio arquivo
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}
