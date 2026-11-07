using AtelieMarylu.Domain.Auditing;

namespace AtelieMarylu.Web.Services;

// Números e alertas da página inicial do painel
public record DashboardSummary(int Paid, int AwaitingPayment, int OutOfStock, IReadOnlyList<AuditEntry> Alerts);
