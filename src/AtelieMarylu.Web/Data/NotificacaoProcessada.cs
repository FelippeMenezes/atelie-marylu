namespace AtelieMarylu.Web.Data;

// Registro de cada notificação de pagamento já processada. O índice único em
// (Provedor, IdEvento) garante que um webhook repetido não seja aplicado duas vezes.
public class NotificacaoProcessada
{
    public long Id { get; set; }
    public string Provedor { get; set; } = "";
    public string IdEvento { get; set; } = "";
    public DateTime RecebidaEm { get; set; } = DateTime.UtcNow;
}
