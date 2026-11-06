namespace AtelieMarylu.Domain.Pagamentos;

// Resultado da leitura de uma notificação (webhook) do provedor.
// IdEvento identifica a notificação: a mesma pode chegar mais de uma vez e só deve ser processada uma.
// ValorPago, quando o provedor informa, é conferido com o valor cobrado.
public record NotificacaoPagamento(string IdEvento, string IdExterno, StatusPagamento Status, decimal? ValorPago = null);
