namespace AtelieMarylu.Domain.Email;

// O e-mail não pôde ser entregue ao provedor (fora do ar, recusou, demorou demais).
// Quem implementa o IEmailClient transforma as falhas dele (HTTP, SMTP, fila...) nesta exceção.
// Assim as telas tratam "o envio falhou" sem saber qual provedor está por trás, e trocar de
// provedor não muda nenhum catch.
//
// IsTransient diz se vale tentar de novo mais tarde (provedor fora do ar, limite de envios, demora) ou se
// repetir daria o mesmo erro (endereço ou conteúdo inválido). Erro de configuração (chave vencida) é passageiro:
// alguém da equipe corrige, e o e-mail sai depois. A outbox usa isso para decidir entre
// agendar outra tentativa e desistir.
public sealed class EmailDeliveryException(string message, Exception? inner = null, bool isTransient = true)
    : Exception(message, inner)
{
    public bool IsTransient { get; } = isTransient;
}
