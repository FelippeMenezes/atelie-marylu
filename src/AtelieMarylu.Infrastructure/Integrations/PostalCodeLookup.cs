using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AtelieMarylu.Domain.Shipping;
using Microsoft.Extensions.Logging;

namespace AtelieMarylu.Infrastructure.Integrations;

// Preenche o endereço a partir do CEP usando o ViaCEP (gratuito, sem chave).
// A chamada sai do servidor, então o navegador não precisa falar com outro domínio.
public class PostalCodeLookup(HttpClient http, ILogger<PostalCodeLookup> logger)
{
    public async Task<PostalCodeAddress?> FindAsync(string postalCode, CancellationToken ct = default)
    {
        if (PostalCode.Normalize(postalCode) is not { } normalized) return null;
        try
        {
            var response = await http.GetFromJsonAsync<ViaCepResponse>($"ws/{normalized}/json/", ct);
            if (response is null || response.IsError == true || string.IsNullOrEmpty(response.City)) return null;
            return new PostalCodeAddress(response.Street ?? "", response.District ?? "", response.City, response.State ?? "");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            // Sem o ViaCEP o cliente só digita o endereço; não é motivo para quebrar a tela
            logger.LogWarning(ex, "ViaCEP indisponível");
            return null;
        }
    }

    // Os nomes em JsonPropertyName são os que o ViaCEP envia
    private sealed record ViaCepResponse(
        [property: JsonPropertyName("logradouro")] string? Street,
        [property: JsonPropertyName("bairro")] string? District,
        [property: JsonPropertyName("localidade")] string? City,
        [property: JsonPropertyName("uf")] string? State,
        [property: JsonPropertyName("erro")] bool? IsError);
}
