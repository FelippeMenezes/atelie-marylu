using AtelieMarylu.Domain.Catalogo;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AtelieMarylu.Infrastructure.Data.Configurations;

public class ProdutoImagemConfiguration : IEntityTypeConfiguration<ProdutoImagem>
{
    public void Configure(EntityTypeBuilder<ProdutoImagem> builder)
    {
        builder.Property(x => x.Url).HasMaxLength(300);
        builder.Property(x => x.UrlMiniatura).HasMaxLength(300);
    }
}
