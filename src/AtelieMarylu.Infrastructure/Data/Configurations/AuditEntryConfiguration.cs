using AtelieMarylu.Domain.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AtelieMarylu.Infrastructure.Data.Configurations;

public class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("AuditEntries");
        builder.Property(x => x.Author).HasMaxLength(256);
        builder.Property(x => x.Action).HasMaxLength(60);
        builder.Property(x => x.Entity).HasMaxLength(40);
        builder.Property(x => x.EntityId).HasMaxLength(60);
        builder.Property(x => x.Details).HasMaxLength(1000);
        builder.HasIndex(x => new { x.Entity, x.EntityId });
        builder.HasIndex(x => x.OccurredAt);
    }
}
