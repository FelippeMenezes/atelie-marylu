namespace AtelieMarylu.Domain.Pagamentos;

// Contrato do provedor de pagamento. Hoje: simulado. Na fase de pagamentos: Mercado Pago.
public interface IGatewayPagamento
{
    string Nome { get; }

    Task<InicioPagamento> IniciarAsync(DadosCobranca cobranca, CancellationToken ct = default);

    // Deve validar a autenticidade da notificação e consultar o status no provedor;
    // nunca confiar apenas no corpo recebido.
    Task<NotificacaoPagamento?> LerNotificacaoAsync(string corpo, IReadOnlyDictionary<string, string> cabecalhos, CancellationToken ct = default);
}
