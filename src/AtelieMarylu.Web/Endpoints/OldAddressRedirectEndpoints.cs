namespace AtelieMarylu.Web.Endpoints;

// As rotas eram em português e passaram para inglês. Os endereços antigos redirecionam com 301
// (permanente): links salvos, favoritos do painel e o Google continuam funcionando, e o Google
// transfere a relevância da página antiga para a nova.
public static class OldAddressRedirectEndpoints
{
    // Endereço antigo → novo. Vale também para o que vem depois (ex.: /produto/5/bolsa → /product/5/bolsa).
    private static readonly (string Old, string New)[] Prefixes =
    [
        ("/meus-pedidos", "/my-orders"),
        ("/produto", "/product"),
        ("/trocas-e-devolucoes", "/returns-policy"),
        ("/politica-de-privacidade", "/privacy-policy"),
        ("/termos-de-uso", "/terms-of-use"),
        ("/painel", "/admin"),
    ];

    // Trechos do caminho dentro do painel (ex.: /painel/pedidos/novo → /admin/orders/new)
    private static readonly Dictionary<string, string> AdminSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pedidos"] = "orders",
        ["produtos"] = "products",
        ["clientes"] = "customers",
        ["usuarios"] = "users",
        ["grupos"] = "groups",
        ["auditoria"] = "audit",
        ["novo"] = "new",
    };

    public static void MapOldAddressRedirects(this WebApplication app)
    {
        foreach (var (oldPrefix, newPrefix) in Prefixes)
        {
            var translateSegments = newPrefix == "/admin";
            IResult Redirect(HttpRequest request, string? rest)
            {
                var path = newPrefix;
                if (!string.IsNullOrEmpty(rest))
                {
                    var segments = rest.Split('/');
                    if (translateSegments)
                        segments = [.. segments.Select(s => AdminSegments.GetValueOrDefault(s, s))];
                    // Cada trecho é codificado de novo, para que nada vindo da URL vire outro endereço
                    path += "/" + string.Join('/', segments.Select(Uri.EscapeDataString));
                }
                return Results.Redirect(path + request.QueryString, permanent: true);
            }

            app.MapGet(oldPrefix, (HttpRequest request) => Redirect(request, null));
            app.MapGet(oldPrefix + "/{**rest}", (HttpRequest request, string? rest) => Redirect(request, rest));
        }
    }
}
