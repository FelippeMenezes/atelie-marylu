using AtelieMarylu.Domain.Pagamentos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AtelieMarylu.Infrastructure.Data.Configurations;

public class PagamentoConfiguration : IEntityTypeConfiguration<Pagamento>
{
    public void Configure(EntityTypeBuilder<Pagamento> builder)
    {
        builder.Property(x => x.Valor).HasPrecision(10, 2);
        builder.Property(x => x.Metodo).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Provedor).HasMaxLength(30);
        builder.Property(x => x.IdExterno).HasMaxLength(100);
        builder.HasIndex(x => new { x.Provedor, x.IdExterno }).IsUnique();
    }
}
