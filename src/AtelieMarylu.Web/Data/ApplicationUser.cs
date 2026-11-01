using Microsoft.AspNetCore.Identity;

namespace AtelieMarylu.Web.Data;

// Dados do perfil além do que o Identity já guarda (e-mail, telefone, senha...).
// [PersonalData] faz o campo entrar no "Baixar meus dados" (LGPD).
public class ApplicationUser : IdentityUser
{
    [PersonalData]
    public string NomeCompleto { get; set; } = "";

    // Opcional no MVP; será exigido para emitir etiqueta de frete (Melhor Envio) e nota fiscal
    [PersonalData]
    public string? Cpf { get; set; }

    [PersonalData]
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;

    // Usado no Admin inicial: a senha veio da configuração e precisa ser trocada no primeiro acesso
    public bool TrocarSenhaObrigatoria { get; set; }
}
