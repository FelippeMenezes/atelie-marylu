namespace AtelieMarylu.Web.Configuracao;

// Seção "LoginGoogle": entrar com a conta Google. Só é ligado quando tem ClientId e ClientSecret
// (criados no Google Cloud; veja docs/LOGIN-GOOGLE.md). Sem as chaves, o botão não aparece.
public class LoginGoogleOptions
{
    public const string Secao = "LoginGoogle";

    // Para onde o navegador é levado ao clicar no botão. Entra no form-action do CSP:
    // sem isso, o navegador bloqueia o redirecionamento que sai do formulário de login.
    public const string EnderecoAutorizacao = "https://accounts.google.com";

    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";

    public bool Ativo => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
