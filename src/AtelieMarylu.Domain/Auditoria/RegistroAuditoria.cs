namespace AtelieMarylu.Domain.Auditoria;

// Quem fez o quê e quando, nas ações do painel e nos eventos de pagamento.
// Só recebe inserções: nunca é alterado nem apagado pela aplicação.
public class RegistroAuditoria
{
    public long Id { get; set; }
    public DateTime Quando { get; set; } = DateTime.UtcNow;

    // Null quando foi o sistema (webhook, expiração automática)
    public string? UsuarioId { get; set; }
    public string Autor { get; set; } = "sistema";

    public string Acao { get; set; } = "";
    public string Entidade { get; set; } = "";
    public string EntidadeId { get; set; } = "";
    public string? Detalhes { get; set; }
}
