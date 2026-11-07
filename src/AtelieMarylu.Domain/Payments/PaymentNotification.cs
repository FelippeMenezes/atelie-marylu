namespace AtelieMarylu.Domain.Payments;

// Resultado da leitura de uma notificação (webhook) do provedor.
// EventId identifica a notificação: a mesma pode chegar mais de uma vez e só deve ser processada uma.
// AmountPaid, quando o provedor informa, é conferido com o valor cobrado.
public record PaymentNotification(string EventId, string ExternalId, PaymentStatus Status, decimal? AmountPaid = null);
