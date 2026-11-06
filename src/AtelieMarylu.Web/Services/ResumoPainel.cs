using AtelieMarylu.Domain.Auditoria;

namespace AtelieMarylu.Web.Services;

// Números e alertas da página inicial do painel
public record ResumoPainel(int Pagos, int AguardandoPagamento, int Esgotados, IReadOnlyList<RegistroAuditoria> Alertas);
