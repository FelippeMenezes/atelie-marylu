namespace AtelieMarylu.Infrastructure.Integrations;

// Guarda a chave pública do webhook do PagBank em memória (singleton), para não buscá-la a cada aviso.
// O gateway é criado de novo a cada uso (HttpClient tipado), por isso a chave mora aqui, e não nele.
public class PagBankPublicKeyCache
{
    // A chave muda raramente (o PagBank só permite renovar depois de 7 dias). Uma hora é folga suficiente.
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    // Depois de buscar, espera um pouco antes de buscar de novo por causa de uma assinatura inválida.
    // Sem isso, avisos forjados em sequência fariam a loja chamar o PagBank a cada um deles.
    public static readonly TimeSpan MinimumRefreshInterval = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim gate = new(1, 1);
    private byte[]? key;
    private DateTimeOffset fetchedAt;

    public async Task<byte[]> GetAsync(Func<CancellationToken, Task<byte[]>> fetch, DateTimeOffset now, bool refresh, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var expired = key is null || now - fetchedAt > Lifetime;
            var refreshAllowed = refresh && now - fetchedAt > MinimumRefreshInterval;
            if (expired || refreshAllowed)
            {
                key = await fetch(ct);
                fetchedAt = now;
            }
            return key!;
        }
        finally
        {
            gate.Release();
        }
    }
}
