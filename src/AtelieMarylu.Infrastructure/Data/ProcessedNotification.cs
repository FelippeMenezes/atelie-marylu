namespace AtelieMarylu.Infrastructure.Data;

// Registro de cada notificação de pagamento já processada. O índice único em
// (Provedor, EventId) garante que um webhook repetido não seja aplicado duas vezes.
public class ProcessedNotification
{
    public long Id { get; set; }
    public string Provider { get; set; } = "";
    public string EventId { get; set; } = "";
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
}
