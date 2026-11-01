using System.Net.Http.Json;
using AtelieMarylu.Domain.Frete;

namespace AtelieMarylu.Web.Integracoes;

public record EnderecoCep(string Logradouro, string Bairro, string Cidade, string Uf);

// Preenche o endereço a partir do CEP usando o ViaCEP (gratuito, sem chave).
// A chamada sai do servidor, então o navegador não precisa falar com outro domínio.
public class ConsultaCep(HttpClient http, ILogger<ConsultaCep> logger)
{
    public async Task<EnderecoCep?> BuscarAsync(string cep, CancellationToken ct = default)
    {
        if (Cep.Normalizar(cep) is not { } normalizado) return null;
        try
        {
            var r = await http.GetFromJsonAsync<RespostaViaCep>($"ws/{normalizado}/json/", ct);
            if (r is null || r.Erro == true || string.IsNullOrEmpty(r.Localidade)) return null;
            return new EnderecoCep(r.Logradouro ?? "", r.Bairro ?? "", r.Localidade, r.Uf ?? "");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            // Sem o ViaCEP o cliente só digita o endereço; não é motivo para quebrar a tela
            logger.LogWarning(ex, "ViaCEP indisponível");
            return null;
        }
    }

    private sealed record RespostaViaCep(string? Logradouro, string? Bairro, string? Localidade, string? Uf, bool? Erro);
}
