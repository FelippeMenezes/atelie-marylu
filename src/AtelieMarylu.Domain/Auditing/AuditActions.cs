namespace AtelieMarylu.Domain.Auditing;

// Nomes das ações registradas na auditoria. Ficam em constantes para um erro de digitação
// virar erro de compilação, e não um registro que nenhuma tela encontra.
public static class AuditActions
{
    public const string OrderCreated = "order.created";
    public const string OrderCancelled = "order.cancelled";
    public const string OrderExpired = "order.expired";
    public const string OrderItemsChanged = "order.items";
    public const string OrderDeliveryChanged = "order.delivery";
    public const string OrderPreparing = "order.preparing";
    public const string OrderShipped = "order.shipped";
    public const string OrderDelivered = "order.delivered";

    public const string PaymentNotified = "payment.notified";
    public const string PaymentMismatch = "payment.mismatch";
    public const string PaymentManual = "payment.manual";
    public const string PaymentReversed = "payment.reversed";

    // Ações que pedem atenção da loja: aparecem em destaque no resumo, no pedido e na auditoria
    public static bool IsPaymentAlert(string action) => action is PaymentMismatch or PaymentReversed;

    public const string ProductCreated = "product.created";
    public const string ProductEdited = "product.edited";

    public const string AccessChanged = "access.changed";
    public const string PasswordReset = "user.password-reset";
    public const string CustomerDeletedByStaff = "user.deleted-by-staff";
    public const string GroupCreated = "group.created";
    public const string GroupPermissionsChanged = "group.permissions";
    public const string GroupDeleted = "group.deleted";
}
