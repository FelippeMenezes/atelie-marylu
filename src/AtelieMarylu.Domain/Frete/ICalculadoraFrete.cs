namespace AtelieMarylu.Domain.Frete;

// Contrato que qualquer provedor de frete precisa cumprir.
// Hoje existe só a implementação simulada; na fase de frete entra a do Melhor Envio.
public interface ICalculadoraFrete
{
    Task<IReadOnlyList<OpcaoFrete>> CotarAsync(string cepDestino, IReadOnlyList<PacoteFrete> pacotes, CancellationToken ct = default);
}
