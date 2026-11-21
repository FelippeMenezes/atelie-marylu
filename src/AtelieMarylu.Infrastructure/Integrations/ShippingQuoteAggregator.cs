using AtelieMarylu.Domain.Shipping;
using Microsoft.Extensions.Logging;

namespace AtelieMarylu.Infrastructure.Integrations;

// Junta as formas de entrega de várias fontes numa lista só (padrão Composite: ele também É um IShippingCalculator).
// Hoje: retirada no ateliê + Melhor Envio, que cota tanto a entrega local (mesma cidade) quanto a distante:
// é o CEP de destino que decide quais modalidades aparecem. Uma fonte nova entra aqui sem mudar o OrderService nem nenhuma tela.
//
// As fontes são consultadas ao mesmo tempo: a cliente espera pela mais lenta, e não pela soma de todas.
public sealed class ShippingQuoteAggregator(IReadOnlyList<IShippingCalculator> sources, ILogger<ShippingQuoteAggregator> logger)
    : IShippingCalculator
{
    public async Task<IReadOnlyList<ShippingOption>> QuoteAsync(string destinationPostalCode, IReadOnlyList<ShippingPackage> packages, CancellationToken ct = default)
    {
        if (PostalCode.Normalize(destinationPostalCode) is null)
            throw new ArgumentException("Invalid postal code.", nameof(destinationPostalCode));

        var results = await Task.WhenAll(sources.Select(source => QuoteOneAsync(source, destinationPostalCode, packages, ct)));
        return Combine(results);
    }

    // Resultado de uma fonte: as opções que ela deu OU a falha (nunca as duas)
    internal sealed record SourceResult(string Source, IReadOnlyList<ShippingOption> Options, ShippingQuoteException? Failure)
    {
        public bool Failed => Failure is not null;
    }

    // A falha de uma fonte não derruba as outras aqui: quem decide o que fazer com ela é o Combine
    private static async Task<SourceResult> QuoteOneAsync(IShippingCalculator source, string postalCode,
        IReadOnlyList<ShippingPackage> packages, CancellationToken ct)
    {
        var name = source.GetType().Name;
        try
        {
            return new SourceResult(name, await source.QuoteAsync(postalCode, packages, ct), null);
        }
        catch (ShippingQuoteException ex)
        {
            return new SourceResult(name, [], ex);
        }
    }

    // Decide o que a cliente vê quando uma ou mais fontes falham. Os resultados vêm na ordem das fontes
    // (a retirada no ateliê primeiro), e a lista devolvida mantém essa ordem.
    //
    // Política (degradação honesta):
    //   1. Toda falha vai para o log: é o único jeito de saber que um provedor caiu (ou que o token venceu).
    //   2. Uma fonte que falhou não derruba as outras: se ainda sobra alguma ENTREGA no endereço
    //      (com mais de um provedor de entrega, um cai e o outro responde), a cliente escolhe entre o que sobrou.
    //   3. Se uma fonte de entrega falhou e não sobrou nenhuma entrega, lança ShippingQuoteException.
    //      Mostrar só "retirar no ateliê" diria a uma cliente de outro estado que a loja não entrega para ela,
    //      e ela iria embora sem saber que era só um problema passageiro. Com o erro, a tela pede para tentar de novo.
    //      Já uma fonte que RESPONDEU sem opções (nenhuma transportadora atende o CEP) não é falha: aí "só retirada" é a verdade.
    //   4. Opção vinda de fora é dado não confiável: sem código, com valor negativo ou com código repetido fica de fora.
    //      Com código repetido, o CreateAsync poderia gravar o preço de uma opção e o nome de outra.
    internal IReadOnlyList<ShippingOption> Combine(IReadOnlyList<SourceResult> results)
    {
        foreach (var failed in results.Where(r => r.Failed))
            logger.LogWarning(failed.Failure, "Frete: a fonte {Fonte} falhou na cotação.", failed.Source);

        var options = new List<ShippingOption>();
        var codes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var option in results.SelectMany(r => r.Options))
        {
            if (string.IsNullOrWhiteSpace(option.Code) || option.Amount < 0 || !codes.Add(option.Code))
            {
                logger.LogWarning("Frete: opção inválida ou repetida descartada ({Codigo}).", option.Code);
                continue;
            }
            options.Add(option);
        }

        var deliveryFailed = results.Any(r => r.Failed);
        var hasDelivery = options.Any(o => o.Code != StudioPickupCalculator.Code);
        if (deliveryFailed && !hasDelivery)
            throw new ShippingQuoteException("Nenhuma fonte de entrega respondeu à cotação.");

        return options;
    }
}
