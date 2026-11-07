namespace AtelieMarylu.Domain.Payments;

public record PaymentStart(string ExternalId, string? PaymentUrl, string? PixCopyPaste);
