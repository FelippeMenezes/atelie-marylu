namespace AtelieMarylu.Web.Services;

public record ResumoUsuario(string Id, string Email, string Nome, bool EhAdmin, bool EhFuncionario, IReadOnlyList<string> Grupos);
