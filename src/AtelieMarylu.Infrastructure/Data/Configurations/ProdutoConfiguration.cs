using AtelieMarylu.Domain.Catalogo;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AtelieMarylu.Infrastructure.Data.Configurations;

public class ProdutoConfiguration : IEntityTypeConfiguration<Produto>
{
    public void Configure(EntityTypeBuilder<Produto> builder)
    {
        builder.Property(x => x.Nome).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Descricao).HasMaxLength(2000);
        builder.Property(x => x.Preco).HasPrecision(10, 2);
        builder.HasMany(x => x.Imagens).WithOne().HasForeignKey(x => x.ProdutoId).OnDelete(DeleteBehavior.Cascade);
        builder.ToTable(t => t.HasCheckConstraint("CK_Produtos_Estoque", "\"Estoque\" >= 0"));
    }
}
