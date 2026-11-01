namespace AtelieMarylu.Domain.Frete;

// Tabela fixa só para o MVP. Será trocada pela cotação real do Melhor Envio.
public class FreteSimulado : ICalculadoraFrete
{
    public const string CodigoRetirada = "retirada";

    public Task<IReadOnlyList<OpcaoFrete>> CotarAsync(string cepDestino, IReadOnlyList<PacoteFrete> pacotes, CancellationToken ct = default)
    {
        if (Cep.Normalizar(cepDestino) is null)
            throw new ArgumentException("CEP inválido.", nameof(cepDestino));

        var quilos = Math.Ceiling(pacotes.Sum(p => p.PesoGramas * p.Quantidade) / 1000m);
        if (quilos < 1) quilos = 1;

        IReadOnlyList<OpcaoFrete> opcoes =
        [
            new(CodigoRetirada, "Retirada no ateliê", 0m, null),
            new("pac", "PAC (simulado)", 20m + 5m * (quilos - 1), 8),
            new("sedex", "SEDEX (simulado)", 35m + 8m * (quilos - 1), 3),
        ];
        return Task.FromResult(opcoes);
    }
}
