using AtelieMarylu.Domain.Seguranca;

namespace AtelieMarylu.Web.Services;

public record ResumoGrupo(string Nome, IReadOnlyList<string> Permissoes, int Membros);
