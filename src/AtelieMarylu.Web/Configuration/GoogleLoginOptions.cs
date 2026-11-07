namespace AtelieMarylu.Web.Configuration;

// Seção "GoogleLogin": entrar com a conta Google. Só é ligado quando tem ClientId e ClientSecret
// (criados no Google Cloud). Sem as chaves, o botão não aparece.
public class GoogleLoginOptions
{
    public const string SectionName = "GoogleLogin";

    // Para onde o navegador é levado ao clicar no botão. Entra no form-action do CSP:
    // sem isso, o navegador bloqueia o redirecionamento que sai do formulário de login.
    public const string AuthorizationEndpoint = "https://accounts.google.com";

    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";

    public bool IsActive => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
