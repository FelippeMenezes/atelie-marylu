using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AtelieMarylu.Infrastructure.Integrations;

// Registro das chamadas ao PagBank para a HOMOLOGAÇÃO. O PagBank pede "os requests e responses das requisições"
// em arquivo (.json, .txt ou .pdf). Fica DESLIGADO por padrão (PagBank:LogTraffic) e só deve ser ligado enquanto
// os cenários da homologação são gerados.
//
// LGPD: as respostas do PagBank trazem nome, e-mail, CPF, telefone e endereço da cliente, e o pedido leva o token
// da loja. Nada disso vai para o arquivo: o token vira "Bearer ***" e os blocos com dados pessoais viram "***".
// Os itens, valores, status e ids continuam legíveis, que é o que a homologação precisa ver.
public class PagBankTrafficRecorder(IOptions<PagBankOptions> options, TimeProvider clock, ILogger<PagBankTrafficRecorder> logger)
{
    // Blocos inteiros com dados de uma pessoa (ou do cartão dela): substituídos por "***"
    private static readonly HashSet<string> PersonalBlocks = new(StringComparer.OrdinalIgnoreCase)
    {
        "customer", "shipping", "holder", "buyer", "payer", "address", "card", "phones", "phone",
    };

    // Campos soltos com dado pessoal, em qualquer lugar do JSON
    private static readonly HashSet<string> PersonalFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "email", "tax_id",
    };

    private int announced;

    public bool IsEnabled => options.Value.LogTraffic;

    public string Directory => string.IsNullOrWhiteSpace(options.Value.TrafficLogDirectory)
        ? Path.Combine(Path.GetTempPath(), "pagbank-homologacao")
        : options.Value.TrafficLogDirectory;

    // Grava um arquivo por chamada. Falhar ao gravar nunca atrapalha o pagamento: vira só um aviso no log.
    public async Task RecordAsync(string operation, JsonObject request, JsonObject? response, CancellationToken ct = default)
    {
        if (!IsEnabled) return;
        if (Interlocked.Exchange(ref announced, 1) == 0)
            logger.LogWarning("Registro de chamadas ao PagBank LIGADO (homologação). Arquivos em {Pasta}. Desligue (PagBank:LogTraffic=false) ao terminar.", Directory);

        var now = clock.GetUtcNow();
        var entry = new JsonObject
        {
            ["timestamp"] = now.ToString("O"),
            ["environment"] = options.Value.Sandbox ? "sandbox" : "production",
            ["operation"] = operation,
            ["request"] = request,
            ["response"] = response,
        };

        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            // Data e hora primeiro (os arquivos ficam em ordem), mais um sufixo aleatório para chamadas no mesmo milissegundo
            var suffix = Guid.NewGuid().ToString("N")[..8];
            var file = Path.Combine(Directory, $"{now:yyyyMMdd-HHmmss-fff}-{operation}-{suffix}.json");
            await File.WriteAllTextAsync(file, entry.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Não foi possível gravar o registro da chamada {Operacao} ao PagBank.", operation);
        }
    }

    // Converte o corpo em JSON mascarado. Corpo que não é JSON não é gravado (não dá para garantir que não tem dado pessoal).
    public static JsonNode? MaskBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return JsonValue.Create($"(corpo não JSON omitido, {body.Length} caracteres)");
        }
        Mask(node);
        return node;
    }

    private static void Mask(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (PersonalBlocks.Contains(key) || PersonalFields.Contains(key))
                        obj[key] = "***";
                    else
                        Mask(obj[key]);
                }
                break;
            case JsonArray array:
                foreach (var item in array)
                    Mask(item);
                break;
        }
    }
}
