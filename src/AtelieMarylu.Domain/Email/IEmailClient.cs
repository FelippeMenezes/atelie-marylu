namespace AtelieMarylu.Domain.Email;

// Contrato do envio de e-mail, como IPaymentGateway e IShippingCalculator: quem monta o e-mail
// (o texto, o idioma) não sabe qual provedor entrega. Hoje é o Resend; trocar de provedor é trocar
// só a implementação registrada no AddInfrastructure.
public interface IEmailClient
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}

// Html já pronto e seguro: quem monta é responsável por codificar o que veio do usuário (ex.: o nome).
public sealed record EmailMessage(string To, string Subject, string Html);
