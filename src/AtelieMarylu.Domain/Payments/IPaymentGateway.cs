namespace AtelieMarylu.Domain.Payments;

// Contrato do provedor de pagamento. Hoje: simulado. Na fase de pagamentos: Mercado Pago.
public interface IPaymentGateway
{
    string Name { get; }

    Task<PaymentStart> StartAsync(ChargeRequest charge, CancellationToken ct = default);

    // Deve validar a autenticidade da notificação e consultar o status no provedor;
    // nunca confiar apenas no corpo recebido.
    Task<PaymentNotification?> ReadNotificationAsync(string body, IReadOnlyDictionary<string, string> headers, CancellationToken ct = default);
}
