using AtelieMarylu.Domain.Shipping;
using Microsoft.Extensions.Caching.Memory;

namespace AtelieMarylu.Infrastructure.Integrations;

// Guarda por alguns minutos as cotações do Melhor Envio que deram certo (padrão das plataformas de e-commerce).
//   - Protege a conta: a calculadora da página do produto não exige login, e sem cache cada clique (ou um robô)
//     seria uma chamada à API com o token da loja. Estourar o limite de chamadas deixaria TODOS os checkouts sem frete.
//   - Mantém o preço: a confirmação do pedido cota de novo, e com o cache ela recebe o mesmo valor que a cliente viu.
//
// Cache PRÓPRIO, com teto de entradas, e não o IMemoryCache compartilhado (que não tem limite): um robô variando
// o CEP não consegue encher a memória do servidor nem expulsar o que outras partes da loja guardam.
// Singleton: vale para o servidor inteiro, como o OrderEvents (com mais de um servidor, cada um tem o seu).
public sealed class MelhorEnvioQuoteCache : IDisposable
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    public const int MaxEntries = 2000;

    private readonly MemoryCache cache;

    public MelhorEnvioQuoteCache(TimeProvider clock) =>
        cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = MaxEntries, Clock = new SystemClockAdapter(clock) });

    public bool TryGet(string key, out IReadOnlyList<ShippingOption> options)
    {
        if (cache.TryGetValue(key, out IReadOnlyList<ShippingOption>? found) && found is not null)
        {
            options = found;
            return true;
        }
        options = [];
        return false;
    }

    // Cada entrada conta 1 no teto. Cheio, o MemoryCache não guarda a nova e dispara, em segundo plano, uma limpeza
    // (compactação) das entradas vencidas e das menos usadas. A cotação continua funcionando, só sem cache naquele momento.
    // Guarda uma CÓPIA somente leitura: a mesma lista vai para todas as clientes por 10 minutos. Uma ReadOnlyCollection
    // recusa acrescentar, tirar e trocar opções (um array comum ainda deixaria trocar pela posição); o ShippingOption
    // já é imutável. Assim, um descuido numa tela não muda a cotação de todo mundo.
    public void Set(string key, IReadOnlyList<ShippingOption> options) =>
        cache.Set<IReadOnlyList<ShippingOption>>(key, Array.AsReadOnly(options.ToArray()),
            new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = Lifetime });

    public void Dispose() => cache.Dispose();

    // O MemoryCache ainda usa o ISystemClock antigo; este adaptador deixa os testes controlarem o tempo
#pragma warning disable CS0618
    private sealed class SystemClockAdapter(TimeProvider clock) : Microsoft.Extensions.Internal.ISystemClock
    {
        public DateTimeOffset UtcNow => clock.GetUtcNow();
    }
#pragma warning restore CS0618
}
