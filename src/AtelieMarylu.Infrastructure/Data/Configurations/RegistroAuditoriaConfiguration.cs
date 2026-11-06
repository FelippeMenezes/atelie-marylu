using AtelieMarylu.Domain.Auditoria;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AtelieMarylu.Infrastructure.Data.Configurations;

public class RegistroAuditoriaConfiguration : IEntityTypeConfiguration<RegistroAuditoria>
{
    public void Configure(EntityTypeBuilder<RegistroAuditoria> builder)
    {
        builder.ToTable("Auditoria");
        builder.Property(x => x.Autor).HasMaxLength(256);
        builder.Property(x => x.Acao).HasMaxLength(60);
        builder.Property(x => x.Entidade).HasMaxLength(40);
        builder.Property(x => x.EntidadeId).HasMaxLength(60);
        builder.Property(x => x.Detalhes).HasMaxLength(1000);
        builder.HasIndex(x => new { x.Entidade, x.EntidadeId });
        builder.HasIndex(x => x.Quando);
    }
}
