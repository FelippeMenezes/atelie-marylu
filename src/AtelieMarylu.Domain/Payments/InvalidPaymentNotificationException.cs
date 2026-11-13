namespace AtelieMarylu.Domain.Payments;

// O aviso recebido no webhook não prova que veio do provedor (assinatura ausente ou inválida).
// O webhook responde 401, e não 400: assim, no log, um ataque se distingue de um aviso mal formado.
public sealed class InvalidPaymentNotificationException(string message) : Exception(message);
