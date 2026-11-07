using AtelieMarylu.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AtelieMarylu.Infrastructure.Data.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.OwnsOne(x => x.Address, e =>
        {
            e.Property(x => x.PostalCode).HasMaxLength(8);
            e.Property(x => x.Street).HasMaxLength(150);
            e.Property(x => x.Number).HasMaxLength(20);
            e.Property(x => x.Complement).HasMaxLength(80);
            e.Property(x => x.District).HasMaxLength(80);
            e.Property(x => x.City).HasMaxLength(80);
            e.Property(x => x.State).HasMaxLength(2);
        });
        builder.HasMany(x => x.Items).WithOne().HasForeignKey("OrderId").OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Payments).WithOne().HasForeignKey(x => x.OrderId);
        builder.Navigation(x => x.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(x => x.Payments).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Property(x => x.Code).HasMaxLength(12);
        builder.HasIndex(x => x.Code).IsUnique();
        builder.Property(x => x.ShippingCost).HasPrecision(10, 2);
        builder.Property(x => x.PixDiscountPercent).HasPrecision(5, 2);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.Notes).HasMaxLength(500);
        builder.Property(x => x.CancellationReason).HasMaxLength(200);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.CustomerId);
        builder.HasIndex(x => new { x.Status, x.PayBy });

        // Controle de concorrência otimista com a coluna de sistema xmin do Postgres:
        // se duas pessoas (ou o webhook e um funcionário) alterarem o mesmo pedido ao mesmo tempo,
        // a segunda gravação falha em vez de sobrescrever a primeira em silêncio.
        builder.Property<uint>("Version").IsRowVersion();

        builder.Ignore(x => x.Subtotal);
        builder.Ignore(x => x.Total);
        builder.Ignore(x => x.PixDiscount);
    }
}
