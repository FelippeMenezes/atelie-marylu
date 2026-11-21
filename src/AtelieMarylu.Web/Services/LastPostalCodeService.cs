using System.Security.Cryptography;
using AtelieMarylu.Domain.Shipping;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.JSInterop;

namespace AtelieMarylu.Web.Services;

// Lembra no navegador o último CEP com que a cliente calculou o frete (produto, carrinho ou checkout),
// para o checkout já abrir com ele. Fica no localStorage, criptografado pelo ASP.NET, como o carrinho:
// funciona antes do login (a cotação na página do produto não exige conta) e não cria dado pessoal no banco.
//
// É só uma conveniência: qualquer falha (storage bloqueado, chaves trocadas, circuito caído) devolve null
// e o campo começa vazio. Depois de 30 dias o CEP é esquecido, para um computador compartilhado não
// sugerir para sempre o CEP de outra pessoa.
public class LastPostalCodeService(ProtectedLocalStorage storage, TimeProvider clock, ILogger<LastPostalCodeService> logger)
{
    private const string StorageKey = "lastPostalCode";
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    public sealed record StoredPostalCode(string PostalCode, DateTime SavedAt);

    // Só os 8 dígitos (ex.: "20560040"), ou null se não houver CEP recente e válido
    public async Task<string?> GetAsync()
    {
        try
        {
            var stored = await storage.GetAsync<StoredPostalCode>(StorageKey);
            if (!stored.Success || stored.Value is null) return null;
            if (clock.GetUtcNow().UtcDateTime - stored.Value.SavedAt > Lifetime) return null;
            return PostalCode.Normalize(stored.Value.PostalCode);
        }
        catch (Exception ex) when (IsStorageFailure(ex))
        {
            logger.LogDebug(ex, "Último CEP cotado indisponível no navegador.");
            return null;
        }
    }

    // Chamar só depois de uma cotação que deu certo: assim fica guardado um CEP que existe e é atendido
    public async Task SaveAsync(string postalCode)
    {
        if (PostalCode.Normalize(postalCode) is not { } normalized) return;
        try
        {
            await storage.SetAsync(StorageKey, new StoredPostalCode(normalized, clock.GetUtcNow().UtcDateTime));
        }
        catch (Exception ex) when (IsStorageFailure(ex))
        {
            logger.LogDebug(ex, "Não foi possível guardar o último CEP cotado no navegador.");
        }
    }

    // As falhas do storage do navegador. Qualquer outra coisa é bug e deve subir.
    private static bool IsStorageFailure(Exception ex) =>
        ex is JSException or JSDisconnectedException or CryptographicException or TaskCanceledException or InvalidOperationException;
}
