using AtelieMarylu.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AtelieMarylu.Infrastructure.Data.Configurations;

public class CancellationRequestConfiguration : IEntityTypeConfiguration<CancellationRequest>
{
    public void Configure(EntityTypeBuilder<CancellationRequest> builder)
    {
        builder.ToTable("CancellationRequests");
        builder.Property(x => x.Protocol).HasMaxLength(12);
        builder.HasIndex(x => x.Protocol).IsUnique();
        builder.Property(x => x.Reason).HasMaxLength(CancellationRequest.ReasonMaxLength);
        builder.Property(x => x.ResolutionNote).HasMaxLength(CancellationRequest.ResolutionNoteMaxLength);
        builder.Property(x => x.OrderStatusAtRequest).HasConversion<string>().HasMaxLength(30);
        // O resumo do painel procura as solicitações sem resposta
        builder.HasIndex(x => x.ResolvedAt);
        builder.Ignore(x => x.IsPending);
    }
}
