using AtelieMarylu.Domain.Auditing;
using AtelieMarylu.Domain.Catalog;
using AtelieMarylu.Domain.Payments;
using AtelieMarylu.Domain.Orders;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AtelieMarylu.Infrastructure.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options), IDataProtectionKeyContext
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<ProcessedNotification> ProcessedNotifications => Set<ProcessedNotification>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    // Chaves que criptografam cookies de login e o carrinho; no banco, sobrevivem a reinícios e deploys
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Lê as classes da pasta Configurations: cada tabela tem o seu próprio arquivo
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}
