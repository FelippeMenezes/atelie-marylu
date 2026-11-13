namespace AtelieMarylu.Domain.Payments;

// O que chegou no webhook, sem nenhum tipo do ASP.NET: corpo, cabeçalhos e query string.
// RawBody são os bytes exatos recebidos: assinaturas (como a do PagBank) são calculadas sobre eles,
// e qualquer releitura do JSON (espaços, ordem dos campos) faria a conferência falhar.
// A query entra porque a loja põe na URL de aviso a referência do pagamento.
public record PaymentWebhookRequest(
    ReadOnlyMemory<byte> RawBody,
    IReadOnlyDictionary<string, string> Headers,
    IReadOnlyDictionary<string, string> Query)
{
    public string Body => System.Text.Encoding.UTF8.GetString(RawBody.Span);
}
