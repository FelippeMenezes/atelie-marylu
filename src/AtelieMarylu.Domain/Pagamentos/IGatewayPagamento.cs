namespace AtelieMarylu.Domain.Pagamentos;

public record DadosCobranca(int PedidoId, string CodigoPedido, decimal Valor, MetodoPagamento Metodo, string EmailCliente, string NomeCliente, string Descricao, DateTime ExpiraEm);

public record InicioPagamento(string IdExterno, string? UrlPagamento, string? PixCopiaECola);

// Resultado da leitura de uma notificação (webhook) do provedor.
// IdEvento identifica a notificação: a mesma pode chegar mais de uma vez e só deve ser processada uma.
// ValorPago, quando o provedor informa, é conferido com o valor cobrado.
public record NotificacaoPagamento(string IdEvento, string IdExterno, StatusPagamento Status, decimal? ValorPago = null);

// Contrato do provedor de pagamento. Hoje: simulado. Na fase de pagamentos: Mercado Pago.
public interface IGatewayPagamento
{
    string Nome { get; }

    Task<InicioPagamento> IniciarAsync(DadosCobranca cobranca, CancellationToken ct = default);

    // Deve validar a autenticidade da notificação e consultar o status no provedor;
    // nunca confiar apenas no corpo recebido.
    Task<NotificacaoPagamento?> LerNotificacaoAsync(string corpo, IReadOnlyDictionary<string, string> cabecalhos, CancellationToken ct = default);
}
