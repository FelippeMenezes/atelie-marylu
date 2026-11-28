using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AtelieMarylu.Infrastructure.Data.Configurations;

public class OutboxEmailConfiguration : IEntityTypeConfiguration<OutboxEmail>
{
    public void Configure(EntityTypeBuilder<OutboxEmail> builder)
    {
        builder.Property(x => x.Kind).HasMaxLength(50);
        builder.Property(x => x.Recipient).HasMaxLength(256);
        builder.Property(x => x.Language).HasMaxLength(10);
        builder.Property(x => x.Payload).HasColumnType("jsonb");
        builder.Property(x => x.LastError).HasMaxLength(500);

        // O worker procura só as pendentes, pela hora da próxima tentativa. O índice parcial fica pequeno:
        // as linhas enviadas, abandonadas ou puladas (a maioria) nem entram nele.
        builder.HasIndex(x => x.NextAttemptAt)
            .HasFilter("\"SentAt\" IS NULL AND \"FailedAt\" IS NULL AND \"SkippedAt\" IS NULL")
            .HasDatabaseName("IX_OutboxEmails_Pending");

        // Limite de "pedido recebido" por destinatário e a exclusão da conta procuram por estas colunas
        builder.HasIndex(x => new { x.Recipient, x.Kind, x.CreatedAt });
        builder.HasIndex(x => x.OrderId);
    }
}
