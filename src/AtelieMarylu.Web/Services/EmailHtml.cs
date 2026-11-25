using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace AtelieMarylu.Web.Services;

// Ferramentas comuns aos e-mails da loja (conta e pedidos)
internal static class EmailHtml
{
    // Transforma um componente de Components/Emails em HTML, fora de uma tela, com o HtmlRenderer
    public static async Task<string> RenderAsync<TComponent>(IServiceProvider services, ILoggerFactory loggerFactory,
        Dictionary<string, object?> parameters) where TComponent : IComponent
    {
        await using var renderer = new HtmlRenderer(services, loggerFactory);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(parameters));
            return output.ToHtmlString();
        });
    }

    // Troca o idioma enquanto o e-mail é montado e devolve o anterior no Dispose. Serve para mensagens que não
    // vão para quem está navegando (ex.: um aviso para a loja sai no idioma padrão, não no da cliente).
    public static IDisposable UseCulture(string name) => new CultureScope(CultureInfo.GetCultureInfo(name));

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo previousCulture = CultureInfo.CurrentCulture;
        private readonly CultureInfo previousUICulture = CultureInfo.CurrentUICulture;

        public CultureScope(CultureInfo culture)
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUICulture;
        }
    }
}
