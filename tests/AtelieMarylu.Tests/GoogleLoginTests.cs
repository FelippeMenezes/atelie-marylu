using AtelieMarylu.Web.Configuration;
using AtelieMarylu.Web.Security;

namespace AtelieMarylu.Tests;

public class GoogleLoginTests
{
    [Fact]
    public void Google_is_active_only_with_id_and_secret()
    {
        Assert.False(new GoogleLoginOptions().IsActive);
        Assert.False(new GoogleLoginOptions { ClientId = "id" }.IsActive);
        Assert.False(new GoogleLoginOptions { ClientSecret = "segredo" }.IsActive);
        Assert.True(new GoogleLoginOptions { ClientId = "id", ClientSecret = "segredo" }.IsActive);
    }

    [Fact]
    public void Without_google_forms_only_post_to_the_store()
    {
        var csp = SecurityHeaders.BuildCsp(false, false, googleLogin: false);
        Assert.Contains("form-action 'self';", csp);
    }

    // Sem o Google no form-action, o navegador bloqueia o redirecionamento do botão "Continuar com Google"
    [Fact]
    public void Active_google_enters_form_action()
    {
        var csp = SecurityHeaders.BuildCsp(false, false, googleLogin: true);
        Assert.Contains("form-action 'self' https://accounts.google.com;", csp);
    }
}
