namespace AtelieMarylu.Domain.Auditing;

// Quem fez o quê e quando, nas ações do painel e nos eventos de pagamento.
// Só recebe inserções: nunca é alterado nem apagado pela aplicação.
public class AuditEntry
{
    public long Id { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    // Null quando foi o sistema (webhook, expiração automática)
    public string? UserId { get; set; }
    public string Author { get; set; } = "system";

    public string Action { get; set; } = "";
    public string Entity { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string? Details { get; set; }
}
