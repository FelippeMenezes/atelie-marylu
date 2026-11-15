using System.Text.Json.Nodes;

namespace AtelieMarylu.Infrastructure.Integrations;

// Fica entre o HttpClient do PagBank e a rede: vê cada chamada e cada resposta e entrega ao PagBankTrafficRecorder.
// Assim o gateway não muda para registrar. Desligado (PagBank:LogTraffic=false), só repassa a chamada.
public class PagBankTrafficHandler(PagBankTrafficRecorder recorder) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (!recorder.IsEnabled)
            return await base.SendAsync(request, ct);

        var requestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        var response = await base.SendAsync(request, ct);

        // Guarda a resposta em memória para o gateway ainda conseguir lê-la depois do registro
        await response.Content.LoadIntoBufferAsync(ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        var headers = new JsonObject();
        if (request.Headers.Authorization is { } auth) headers["Authorization"] = $"{auth.Scheme} ***";
        if (request.Headers.TryGetValues("x-idempotency-key", out var keys)) headers["x-idempotency-key"] = string.Join(",", keys);
        if (request.Content?.Headers.ContentType is { } type) headers["Content-Type"] = type.ToString();

        await recorder.RecordAsync(OperationName(request), new JsonObject
        {
            ["method"] = request.Method.Method,
            ["url"] = request.RequestUri?.ToString(),
            ["headers"] = headers,
            ["body"] = PagBankTrafficRecorder.MaskBody(requestBody),
        }, new JsonObject
        {
            ["status"] = (int)response.StatusCode,
            ["body"] = PagBankTrafficRecorder.MaskBody(responseBody),
        }, ct);

        return response;
    }

    // Nome legível para o arquivo, a partir do método e do caminho
    public static string OperationName(HttpRequestMessage request)
    {
        var path = request.RequestUri?.AbsolutePath.Trim('/') ?? "";
        var post = request.Method == HttpMethod.Post;
        return path switch
        {
            "checkouts" when post => "criar-checkout",
            var p when p.StartsWith("checkouts/") && p.EndsWith("/inactivate") => "inativar-checkout",
            var p when p.StartsWith("checkouts/") => "consultar-checkout",
            var p when p.StartsWith("orders/") => "consultar-pedido",
            var p when p.StartsWith("public-keys") => "consultar-chave-publica",
            _ => "outra-chamada",
        };
    }
}
