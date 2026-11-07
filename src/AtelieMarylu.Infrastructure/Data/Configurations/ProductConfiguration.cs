using AtelieMarylu.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AtelieMarylu.Infrastructure.Data.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.Property(x => x.Name).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.Property(x => x.Price).HasPrecision(10, 2);
        builder.HasMany(x => x.Images).WithOne().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        builder.ToTable(t => t.HasCheckConstraint("CK_Products_Stock", "\"Stock\" >= 0"));
    }
}
