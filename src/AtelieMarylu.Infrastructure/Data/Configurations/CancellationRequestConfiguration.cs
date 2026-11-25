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
        // No máximo uma solicitação aberta por pedido, garantido pelo banco (índice único parcial do Postgres):
        // vale para qualquer caminho de código, inclusive duas gravações ao mesmo tempo. O OrderService traduz
        // a violação deste índice, pelo nome, em mensagem para a cliente. Não renomear sem ajustar lá.
        // Índice comum da chave estrangeira, declarado à parte: o parcial abaixo só cobre as abertas, e carregar
        // o histórico de um pedido (Include) precisa achar também as respondidas
        builder.HasIndex(x => x.OrderId);
        builder.HasIndex(x => x.OrderId, "IX_CancellationRequests_OrderId_Open")
            .IsUnique()
            .HasFilter("\"ResolvedAt\" IS NULL");
        // O resumo do painel procura as solicitações sem resposta
        builder.HasIndex(x => x.ResolvedAt);
        builder.Ignore(x => x.IsPending);
    }
}
