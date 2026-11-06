namespace AtelieMarylu.Web.Services;

public record AcessoUsuario(bool EhAdmin, IReadOnlyList<string> Grupos, IReadOnlyList<string> PermissoesIndividuais);
