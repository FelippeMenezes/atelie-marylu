using AtelieMarylu.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AtelieMarylu.Infrastructure.Data.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.Property(x => x.Amount).HasPrecision(10, 2);
        builder.Property(x => x.Method).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Provider).HasMaxLength(30);
        builder.Property(x => x.ExternalId).HasMaxLength(100);
        builder.HasIndex(x => new { x.Provider, x.ExternalId }).IsUnique();
    }
}
