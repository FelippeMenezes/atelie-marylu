using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AtelieMarylu.Domain.Shipping;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AtelieMarylu.Infrastructure.Integrations;

// Cotação real pelo Melhor Envio (POST /api/v2/me/shipment/calculate). Mesmo padrão do PagBank e do Resend:
// HttpClient + Options, sem SDK. A cotação só consulta preços: não compra etiqueta nem move dinheiro.
//
// O Melhor Envio devolve uma lista com TODAS as modalidades pedidas. As que não atendem o trecho (ou não aceitam
// o tamanho do pacote) vêm com o campo "error" preenchido e ficam de fora. Lista vazia não é falha: quer dizer
// que nenhuma transportadora atende aquele CEP. Falha é não conseguir perguntar (rede, token, resposta estranha).
public sealed class MelhorEnvioShippingCalculator(
    HttpClient http,
    IOptions<MelhorEnvioOptions> options,
    MelhorEnvioQuoteCache cache,
    ILogger<MelhorEnvioShippingCalculator> logger) : IShippingCalculator
{
    // Código gravado no pedido: "me-" + id da modalidade no Melhor Envio (ex.: "me-1" é o PAC).
    // O id é estável, então a recotação de um pedido encontra a mesma modalidade.
    public const string CodePrefix = "me-";

    private const int MaxNameLength = 80;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // O Melhor Envio manda os preços como texto ("23.50")
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public async Task<IReadOnlyList<ShippingOption>> QuoteAsync(string destinationPostalCode, IReadOnlyList<ShippingPackage> packages, CancellationToken ct = default)
    {
        var to = PostalCode.Normalize(destinationPostalCode)
            ?? throw new ArgumentException("Invalid postal code.", nameof(destinationPostalCode));
        if (packages.Count == 0) return [];

        var settings = options.Value;

        // Mesmo destino, mesmos pacotes e mesma lista de modalidades = mesma cotação (veja MelhorEnvioQuoteCache)
        var cacheKey = CacheKey(settings, to, packages);
        if (cache.TryGet(cacheKey, out var cached)) return cached;

        var body = new CalculateRequest(
            From: new PostalCodeOnly(settings.NormalizedOriginPostalCode!),
            To: new PostalCodeOnly(to),
            // Cada produto vai com as próprias medidas, e o Melhor Envio monta as caixas
            Products: packages.Select((p, index) => new QuoteProduct(
                Id: (index + 1).ToString(CultureInfo.InvariantCulture),
                Width: p.WidthCm,
                Height: p.HeightCm,
                Length: p.LengthCm,
                Weight: p.WeightGrams / 1000m,
                InsuranceValue: p.UnitPrice,
                Quantity: p.Quantity)).ToList(),
            // Aviso de recebimento e mão própria custam a mais: a loja não oferece
            Options: new QuoteOptions(Receipt: false, OwnHand: false),
            Services: string.IsNullOrWhiteSpace(settings.Services) ? null : settings.Services.Replace(" ", ""));

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v2/me/shipment/calculate")
        {
            Content = JsonContent.Create(body, options: Json),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        // Exigido pelo Melhor Envio: nome da aplicação e e-mail de contato técnico
        request.Headers.TryAddWithoutValidation("User-Agent", $"AtelieMarylu ({settings.ContactEmail})");

        var services = await SendAsync(request, ct);

        var quoted = new List<ShippingOption>();
        foreach (var service in services)
        {
            if (!string.IsNullOrWhiteSpace(service.Error))
            {
                // Só o id: o texto do erro vem de fora e poderia forjar linhas de log
                logger.LogDebug("Melhor Envio: modalidade {Id} sem cotação para este trecho.", service.Id);
                continue;
            }

            // custom_* já inclui o que a Marylu configurar no painel do Melhor Envio (dias extras de postagem,
            // acréscimo ou desconto no preço). Sem configuração, é igual ao valor padrão.
            var amount = service.CustomPrice ?? service.Price;
            if (service.Id <= 0 || amount is not > 0m) continue;

            // Prazo zero ou negativo não é um prazo que a cliente entenda ("até 0 dias úteis"): fica sem prazo
            // informado, e a opção continua aparecendo
            var days = service.CustomDeliveryTime ?? service.DeliveryTime;

            quoted.Add(new ShippingOption(
                $"{CodePrefix}{service.Id}",
                NameFor(service),
                Math.Round(amount.Value, 2, MidpointRounding.AwayFromZero),
                days is > 0 ? days : null));
        }

        IReadOnlyList<ShippingOption> result = quoted.OrderBy(o => o.Amount).ToList();
        // Só o que deu certo vai para o cache (lista vazia também: "ninguém atende este CEP" é uma resposta).
        // Falha nunca é guardada, para a próxima tentativa perguntar de novo.
        cache.Set(cacheKey, result);
        return result;
    }

    private static string CacheKey(MelhorEnvioOptions settings, string destination, IReadOnlyList<ShippingPackage> packages) =>
        string.Join('|',
            settings.NormalizedOriginPostalCode, destination, settings.Services.Replace(" ", ""),
            string.Join(';', packages.Select(p => string.Create(CultureInfo.InvariantCulture,
                $"{p.WeightGrams}x{p.HeightCm}x{p.WidthCm}x{p.LengthCm}x{p.Quantity}x{p.UnitPrice}"))));

    // Ex.: "Correios PAC", "Jadlog .Package", "Loggi Coleta". Texto de terceiro: o Razor codifica ao mostrar.
    // Algumas modalidades já trazem a transportadora no nome ("Loggi Ponto"): aí não repete ("Loggi Loggi Ponto").
    private static string NameFor(ServiceQuote service)
    {
        var company = service.Company?.Name?.Trim() ?? "";
        var serviceName = service.Name?.Trim() ?? "";
        var name = company.Length > 0 && !serviceName.StartsWith(company, StringComparison.OrdinalIgnoreCase)
            ? $"{company} {serviceName}".Trim()
            : serviceName;
        if (name.Length == 0) name = $"Melhor Envio {service.Id}";
        return name.Length <= MaxNameLength ? name : name[..MaxNameLength];
    }

    // Envia e lê a resposta. Toda falha vira ShippingQuoteException, menos o cancelamento pedido por quem chamou.
    private async Task<List<ServiceQuote>> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                // Token vencido ou apagado derruba TODAS as cotações por transportadora: precisa de atenção logo
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    logger.LogError("ALERTA: o Melhor Envio recusou o token ({Status}). Gere outro no painel e atualize MelhorEnvio:Token.",
                        (int)response.StatusCode);
                throw new ShippingQuoteException($"O Melhor Envio recusou a cotação ({(int)response.StatusCode}).");
            }

            return await response.Content.ReadFromJsonAsync<List<ServiceQuote>>(Json, ct)
                ?? throw new ShippingQuoteException("O Melhor Envio devolveu uma cotação vazia.");
        }
        catch (HttpRequestException ex)
        {
            throw new ShippingQuoteException("Não foi possível falar com o Melhor Envio.", ex);
        }
        catch (JsonException ex)
        {
            throw new ShippingQuoteException("Resposta inesperada do Melhor Envio.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new ShippingQuoteException("O Melhor Envio demorou demais para cotar o frete.", ex);
        }
    }

    private sealed record CalculateRequest(PostalCodeOnly From, PostalCodeOnly To, List<QuoteProduct> Products, QuoteOptions Options, string? Services);

    private sealed record PostalCodeOnly(string PostalCode);

    private sealed record QuoteProduct(string Id, int Width, int Height, int Length, decimal Weight, decimal InsuranceValue, int Quantity);

    private sealed record QuoteOptions(bool Receipt, bool OwnHand);

    private sealed record ServiceQuote(
        int Id,
        string? Name,
        decimal? Price,
        decimal? CustomPrice,
        int? DeliveryTime,
        int? CustomDeliveryTime,
        string? Error,
        Company? Company);

    private sealed record Company(string? Name);
}
