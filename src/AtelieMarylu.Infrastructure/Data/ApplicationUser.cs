using Microsoft.AspNetCore.Identity;

namespace AtelieMarylu.Infrastructure.Data;

// Dados do perfil além do que o Identity já guarda (e-mail, telefone, senha...).
// [PersonalData] faz o campo entrar no "Baixar meus dados" (LGPD).
public class ApplicationUser : IdentityUser
{
    [PersonalData]
    public string FullName { get; set; } = "";

    // Opcional no MVP; será exigido para emitir etiqueta de frete (Melhor Envio) e nota fiscal
    [PersonalData]
    public string? TaxId { get; set; }

    [PersonalData]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Usado no Admin inicial: a senha veio da configuração e precisa ser trocada no primeiro acesso
    public bool MustChangePassword { get; set; }
}
