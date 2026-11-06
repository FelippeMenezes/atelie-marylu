using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AtelieMarylu.Infrastructure.Data.Configurations;

public class NotificacaoProcessadaConfiguration : IEntityTypeConfiguration<NotificacaoProcessada>
{
    public void Configure(EntityTypeBuilder<NotificacaoProcessada> builder)
    {
        builder.Property(x => x.Provedor).HasMaxLength(30);
        builder.Property(x => x.IdEvento).HasMaxLength(150);
        builder.HasIndex(x => new { x.Provedor, x.IdEvento }).IsUnique();
    }
}
