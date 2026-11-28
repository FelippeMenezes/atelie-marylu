namespace AtelieMarylu.Domain.Email;

// Contrato do envio de e-mail, como IPaymentGateway e IShippingCalculator: quem monta o e-mail
// (o texto, o idioma) não sabe qual provedor entrega. Hoje é o Resend; trocar de provedor é trocar
// só a implementação registrada no AddInfrastructure.
public interface IEmailClient
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}

// Html já pronto e seguro: quem monta é responsável por codificar o que veio do usuário (ex.: o nome).
// IdempotencyKey: a mesma chave, reenviada (ex.: o worker caiu depois de enviar e antes de marcar como enviado),
// não gera um segundo e-mail. O Resend guarda a chave por 24 horas. Os e-mails da outbox usam o Id da linha.
public sealed record EmailMessage(string To, string Subject, string Html, string? IdempotencyKey = null);
