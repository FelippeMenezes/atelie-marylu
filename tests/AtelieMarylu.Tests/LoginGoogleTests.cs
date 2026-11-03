using AtelieMarylu.Web.Configuracao;
using AtelieMarylu.Web.Seguranca;

namespace AtelieMarylu.Tests;

public class LoginGoogleTests
{
    [Fact]
    public void Google_so_fica_ativo_com_id_e_segredo()
    {
        Assert.False(new LoginGoogleOptions().Ativo);
        Assert.False(new LoginGoogleOptions { ClientId = "id" }.Ativo);
        Assert.False(new LoginGoogleOptions { ClientSecret = "segredo" }.Ativo);
        Assert.True(new LoginGoogleOptions { ClientId = "id", ClientSecret = "segredo" }.Ativo);
    }

    [Fact]
    public void Sem_Google_os_formularios_so_vao_para_a_propria_loja()
    {
        var csp = CabecalhosSeguranca.MontarCsp(false, false, loginGoogle: false);
        Assert.Contains("form-action 'self';", csp);
    }

    // Sem o Google no form-action, o navegador bloqueia o redirecionamento do botão "Continuar com Google"
    [Fact]
    public void Google_ativo_entra_no_form_action()
    {
        var csp = CabecalhosSeguranca.MontarCsp(false, false, loginGoogle: true);
        Assert.Contains("form-action 'self' https://accounts.google.com;", csp);
    }
}
