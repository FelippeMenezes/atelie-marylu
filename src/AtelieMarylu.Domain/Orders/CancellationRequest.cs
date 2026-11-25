namespace AtelieMarylu.Domain.Orders;

// Pedido de cancelamento ou desistência feito pela cliente em "Meus pedidos" (Decreto 7.962/2013, art. 5º:
// a desistência tem de poder ser feita pela mesma ferramenta da compra, e a loja confirma o recebimento na hora).
// A solicitação não cancela nada sozinha: ela avisa a equipe, que combina a devolução e faz o estorno no provedor.
public class CancellationRequest
{
    public const int ReasonMaxLength = 500;
    public const int ResolutionNoteMaxLength = 300;

    private CancellationRequest() { } // EF Core

    internal CancellationRequest(OrderStatus orderStatus, string? reason, DateTime requestedAt)
    {
        OrderStatusAtRequest = orderStatus;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (Reason is { Length: > ReasonMaxLength })
            throw new OrderRuleException("The reason can have at most {0} characters.", ReasonMaxLength);
        RequestedAt = requestedAt;
    }

    public int Id { get; private set; }
    public int OrderId { get; private set; }

    // Número de protocolo mostrado à cliente na tela e no e-mail de confirmação
    public string Protocol { get; private set; } = OrderCodeGenerator.Create("CAN");

    public DateTime RequestedAt { get; private set; }
    public string? Reason { get; private set; }

    // Antes do envio é cancelamento; depois do envio é desistência com devolução da peça.
    // Guardado para a equipe saber em que ponto o pedido estava quando a cliente pediu.
    public OrderStatus OrderStatusAtRequest { get; private set; }

    public DateTime? ResolvedAt { get; private set; }
    public string? ResolutionNote { get; private set; }

    public bool IsPending => ResolvedAt is null;

    internal void Resolve(string? note, DateTime resolvedAt)
    {
        if (!IsPending) throw new OrderRuleException("This cancellation request was already answered.");
        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        ResolutionNote = trimmed is { Length: > ResolutionNoteMaxLength } ? trimmed[..ResolutionNoteMaxLength] : trimmed;
        ResolvedAt = resolvedAt;
    }
}
