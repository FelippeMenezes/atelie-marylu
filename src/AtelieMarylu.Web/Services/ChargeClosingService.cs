using AtelieMarylu.Domain.Payments;

namespace AtelieMarylu.Web.Services;

// Lê a fila e fecha cada cobrança no provedor, tentando de novo se ele falhar. Roda fora das requisições,
// então o webhook e as telas do painel não esperam o provedor.
public class ChargeClosingService(
    ChargeClosingQueue queue,
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<ChargeClosingService> logger) : BackgroundService
{
    // Esperas entre as tentativas. Os testes passam esperas zeradas.
    public IReadOnlyList<TimeSpan> RetryDelays { get; init; } = [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)];

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => ProcessQueueAsync(stoppingToken);

    // O laço da fila. Público para o teste chamá-lo direto: desde o .NET 10, o BackgroundService roda o ExecuteAsync
    // em outra thread, então ligar e desligar o serviço no teste vira uma corrida (o desligamento pode chegar antes
    // de o laço começar).
    public async Task ProcessQueueAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var charge in queue.Reader.ReadAllAsync(stoppingToken))
                await CloseAsync(charge, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // A loja está desligando (Ctrl+C, deploy ou falha na subida): parar aqui é o esperado, não um erro.
            // Sem este catch, o host registrava "BackgroundService failed" com log fatal a cada desligamento.
        }
    }

    public async Task CloseAsync(ChargeToClose charge, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            // O checkout vence sozinho no prazo do pedido: depois dele, não há o que fechar
            if (clock.GetUtcNow().UtcDateTime > charge.PayBy) return;
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IPaymentGateway>().CancelChargeAsync(charge.ProviderReference, ct);
                return;
            }
            catch (PaymentGatewayException ex) when (attempt < RetryDelays.Count)
            {
                logger.LogWarning(ex, "Falha ao inativar a cobrança {Referencia}; tentando de novo.", charge.ProviderReference);
                await Task.Delay(RetryDelays[attempt], clock, ct);
            }
            catch (PaymentGatewayException ex)
            {
                logger.LogError(ex, "ALERTA: não foi possível inativar a cobrança {Referencia} do pedido {PedidoId} no provedor. Ela aceita pagamento até {Prazo}.",
                    charge.ProviderReference, charge.OrderId, charge.PayBy);
                return;
            }
        }
    }
}
