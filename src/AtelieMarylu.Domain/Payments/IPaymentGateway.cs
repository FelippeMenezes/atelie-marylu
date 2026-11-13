namespace AtelieMarylu.Domain.Payments;

// Contrato do provedor de pagamento: hoje o simulado; o real será o PagBank.
public interface IPaymentGateway
{
    string Name { get; }

    // true quando o gateway confere cada aviso antes de aceitá-lo: consultando o provedor com a credencial da loja
    // (e a assinatura, quando o provedor manda). O simulado aceita o que chegar, por isso o webhook dele só existe em
    // Development. O nome é "verifica", e não "autentica": os avisos do PagBank não vêm assinados.
    bool VerifiesNotifications { get; }

    // Falhas de comunicação com o provedor viram PaymentGatewayException.
    Task<PaymentStart> StartAsync(ChargeRequest charge, CancellationToken ct = default);

    // Fecha no provedor uma cobrança que deixou de valer (outro pagamento foi iniciado, o pedido foi
    // cancelado ou editado), para a cliente não conseguir pagá-la. Sem cobrança aberta, não faz nada.
    Task CancelChargeAsync(string? providerReference, CancellationToken ct = default);

    // Conciliação: pergunta ao provedor como está a cobrança, sem esperar o aviso. Webhook não é garantia
    // de entrega (ex.: a loja fora do ar durante um deploy), então a loja confere antes de decisões que
    // não voltam atrás, como expirar o pedido. Retorna null quando não há nada a informar.
    Task<PaymentNotification?> QueryStatusAsync(string externalId, string? providerReference, CancellationToken ct = default);

    // Qual pagamento da loja (ExternalId) o aviso diz ser, lido só do que chegou, sem chamar ninguém.
    // O webhook confere no banco se ele existe ANTES de qualquer consulta ao provedor: sem isso, qualquer POST
    // faria a loja gastar a credencial dela (e o provedor poderia bloqueá-la). null = o aviso não diz.
    string? ClaimedExternalId(PaymentWebhookRequest request);

    // Deve validar a autenticidade da notificação e consultar o status no provedor;
    // nunca confiar apenas no corpo recebido. Lança InvalidPaymentNotificationException quando o aviso
    // não prova que veio do provedor, e retorna null quando o aviso é autêntico mas não interessa.
    Task<PaymentNotification?> ReadNotificationAsync(PaymentWebhookRequest request, CancellationToken ct = default);
}
