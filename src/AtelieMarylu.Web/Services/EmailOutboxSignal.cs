namespace AtelieMarylu.Web.Services;

// Acorda o EmailOutboxDispatcher logo depois de uma gravação com e-mail na outbox, para a cliente não esperar a
// próxima rodada. É só um atalho: se o aviso se perder (ex.: a loja reiniciou), a rodada periódica envia do mesmo jeito.
public sealed class EmailOutboxSignal
{
    // No máximo 1 aviso guardado: dez gravações seguidas acordam o worker uma vez só, e ele envia todas
    private readonly SemaphoreSlim semaphore = new(0, 1);

    public void Wake()
    {
        try
        {
            semaphore.Release();
        }
        catch (SemaphoreFullException)
        {
            // Já havia um aviso guardado: o worker vai acordar de qualquer jeito
        }
    }

    // Espera um aviso ou o tempo máximo, o que vier primeiro
    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct) => semaphore.WaitAsync(timeout, ct);
}
