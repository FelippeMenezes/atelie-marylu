namespace AtelieMarylu.Domain.Email;

// O e-mail não pôde ser entregue ao provedor (fora do ar, recusou, demorou demais).
// Quem implementa o IEmailClient transforma as falhas dele (HTTP, SMTP, fila...) nesta exceção.
// Assim as telas tratam "o envio falhou" sem saber qual provedor está por trás, e trocar de
// provedor não muda nenhum catch.
public sealed class EmailDeliveryException(string message, Exception? inner = null) : Exception(message, inner);
