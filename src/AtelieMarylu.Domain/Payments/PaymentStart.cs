namespace AtelieMarylu.Domain.Payments;

// ProviderReference: id da cobrança aberta no provedor (ex.: o checkout CHEC_... do PagBank).
// É por ele que a loja fecha a cobrança no provedor quando o pagamento deixa de valer.
public record PaymentStart(string ExternalId, string? PaymentUrl, string? PixCopyPaste, string? ProviderReference = null);
