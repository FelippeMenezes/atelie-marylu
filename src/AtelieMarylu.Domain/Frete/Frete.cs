namespace AtelieMarylu.Domain.Frete;

public record PacoteFrete(int PesoGramas, int AlturaCm, int LarguraCm, int ComprimentoCm, int Quantidade);

public record OpcaoFrete(string Codigo, string Nome, decimal Valor, int? PrazoDias);

// Contrato que qualquer provedor de frete precisa cumprir.
// Hoje existe só a implementação simulada; na fase de frete entra a do Melhor Envio.
public interface ICalculadoraFrete
{
    Task<IReadOnlyList<OpcaoFrete>> CotarAsync(string cepDestino, IReadOnlyList<PacoteFrete> pacotes, CancellationToken ct = default);
}

public static class Cep
{
    public static string? Normalizar(string? cep)
    {
        var digitos = new string((cep ?? "").Where(char.IsDigit).ToArray());
        return digitos.Length == 8 ? digitos : null;
    }
}
