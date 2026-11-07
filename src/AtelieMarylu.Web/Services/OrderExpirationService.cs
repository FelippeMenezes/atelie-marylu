namespace AtelieMarylu.Web.Services;

// A cada 5 minutos, cancela pedidos não pagos dentro do prazo e devolve o estoque à loja.
// Sem isso, pedidos abandonados (ou feitos de má-fé) deixariam produtos "esgotados" para sempre.
public class OrderExpirationService(IServiceScopeFactory scopes, ILogger<OrderExpirationService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var cancelledCount = await scope.ServiceProvider.GetRequiredService<OrderService>().ExpireOverdueAsync(stoppingToken);
                if (cancelledCount > 0)
                    logger.LogInformation("{Quantidade} pedido(s) cancelado(s) por falta de pagamento.", cancelledCount);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Um erro numa rodada não pode derrubar a tarefa: tenta de novo na próxima
                logger.LogError(ex, "Falha ao expirar pedidos");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
