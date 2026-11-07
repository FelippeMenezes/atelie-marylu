using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AtelieMarylu.Infrastructure.Data.Configurations;

public class ProcessedNotificationConfiguration : IEntityTypeConfiguration<ProcessedNotification>
{
    public void Configure(EntityTypeBuilder<ProcessedNotification> builder)
    {
        builder.Property(x => x.Provider).HasMaxLength(30);
        builder.Property(x => x.EventId).HasMaxLength(150);
        builder.HasIndex(x => new { x.Provider, x.EventId }).IsUnique();
    }
}
