using AtelieMarylu.Domain.Pedidos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AtelieMarylu.Infrastructure.Data.Configurations;

public class PedidoConfiguration : IEntityTypeConfiguration<Pedido>
{
    public void Configure(EntityTypeBuilder<Pedido> builder)
    {
        builder.OwnsOne(x => x.Endereco, e =>
        {
            e.Property(x => x.Cep).HasMaxLength(8);
            e.Property(x => x.Logradouro).HasMaxLength(150);
            e.Property(x => x.Numero).HasMaxLength(20);
            e.Property(x => x.Complemento).HasMaxLength(80);
            e.Property(x => x.Bairro).HasMaxLength(80);
            e.Property(x => x.Cidade).HasMaxLength(80);
            e.Property(x => x.Uf).HasMaxLength(2);
        });
        builder.HasMany(x => x.Itens).WithOne().HasForeignKey("PedidoId").OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Pagamentos).WithOne().HasForeignKey(x => x.PedidoId);
        builder.Navigation(x => x.Itens).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(x => x.Pagamentos).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Property(x => x.Codigo).HasMaxLength(12);
        builder.HasIndex(x => x.Codigo).IsUnique();
        builder.Property(x => x.FreteValor).HasPrecision(10, 2);
        builder.Property(x => x.PercentualDescontoPix).HasPrecision(5, 2);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.Observacao).HasMaxLength(500);
        builder.Property(x => x.MotivoCancelamento).HasMaxLength(200);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ClienteId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.ClienteId);
        builder.HasIndex(x => new { x.Status, x.PagarAte });

        // Controle de concorrência otimista com a coluna de sistema xmin do Postgres:
        // se duas pessoas (ou o webhook e um funcionário) alterarem o mesmo pedido ao mesmo tempo,
        // a segunda gravação falha em vez de sobrescrever a primeira em silêncio.
        builder.Property<uint>("Versao").IsRowVersion();

        builder.Ignore(x => x.Subtotal);
        builder.Ignore(x => x.Total);
        builder.Ignore(x => x.DescontoPix);
    }
}
