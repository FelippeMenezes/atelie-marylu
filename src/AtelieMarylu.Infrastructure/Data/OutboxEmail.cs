namespace AtelieMarylu.Infrastructure.Data;

// Outbox transacional dos e-mails (AML-0041). Quem muda um pedido grava aqui a intenção de enviar, no mesmo
// DbContext e na mesma transação da mudança: ou as duas coisas ficam gravadas, ou nenhuma. O EmailOutboxDispatcher
// (hosted service) lê as linhas pendentes, monta o e-mail e entrega, tentando de novo quando o provedor falha.
//
// Guarda os DADOS do e-mail (Payload), não o HTML pronto: o texto é montado na hora do envio, no idioma gravado
// em Language. O payload é uma "foto" do pedido no momento do evento, então o e-mail não muda se o pedido mudar
// depois.
//
// Dado pessoal expira, o fato fica. Depois de EmailOutboxDispatcher.PersonalDataRetention (30 dias), a linha encerrada
// perde o destinatário, o payload e o erro (minimização, LGPD art. 6º, III), mas guarda tipo, pedido e datas: é a
// prova de que a loja mandou a confirmação que o Decreto 7.962/2013 exige, caso a cliente reclame meses depois.
// A linha inteira só sai depois de EvidenceRetention (5 anos, o prazo do CDC, art. 27) ou na exclusão da conta.
public class OutboxEmail
{
    // UUID v7: ordenado pelo tempo (bom para o índice) e serve de chave de idempotência no provedor
    public Guid Id { get; set; } = Guid.CreateVersion7();

    // Que e-mail é (ex.: "order.paid"). Os nomes ficam em OutboxEmailKinds, no projeto Web.
    public string Kind { get; set; } = "";
    // Null depois da limpeza dos dados pessoais (linha já encerrada)
    public string? Recipient { get; set; }
    public string Language { get; set; } = "";
    public string Payload { get; set; } = "{}";

    // Para apagar as linhas de um pedido e contar envios por cliente, sem ler o JSON
    public int? OrderId { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public int Attempts { get; set; }
    public DateTime? SentAt { get; set; }

    // Desistiu de enviar (erro definitivo ou tentativas esgotadas). Fica para a equipe conferir.
    public DateTime? FailedAt { get; set; }

    // Não enviou de propósito, porque o e-mail perdeu o sentido antes de sair (ex.: "pedido recebido" de um pedido
    // que já foi pago ou expirou). Não é falha: não gera ALERTA. O motivo fica em LastError.
    public DateTime? SkippedAt { get; set; }
    public string? LastError { get; set; }
}
