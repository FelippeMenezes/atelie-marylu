using System.Text;
using System.Xml;
using AtelieMarylu.Web.Configuration;
using AtelieMarylu.Web.Seo;
using AtelieMarylu.Web.Services;
using Microsoft.Extensions.Options;

namespace AtelieMarylu.Web.Endpoints;

// robots.txt e sitemap.xml: dizem ao Google o que indexar (vitrine e produtos) e o que não (conta, painel, checkout).
// products.xml: o catálogo para o Google Merchant Center.
public static class SeoEndpoints
{
    public const string CachePolicy = "Seo";

    // Estes endereços são públicos e consultam o banco: sem cache, qualquer um poderia martelá-los
    // (o limitador de chamadas só olha POST). O Google busca o feed uma vez por dia, então 1 hora de
    // atraso numa mudança de preço ou estoque não tem efeito prático. O cache só guarda respostas 200
    // e separa por domínio (o endereço da loja vai dentro do XML).
    // A query string fica FORA da chave: o padrão do .NET inclui todos os parâmetros, e aí "?r=1", "?r=2"...
    // iam ao banco a cada chamada e enchiam a memória do cache (achado na revisão do AML-0034).
    // Nenhum destes endereços lê parâmetro, então ignorar a query não muda a resposta.
    public static IServiceCollection AddSeoOutputCache(this IServiceCollection services) =>
        services.AddOutputCache(options =>
            options.AddPolicy(CachePolicy, policy => policy
                .Expire(TimeSpan.FromHours(1))
                .SetVaryByQuery([])));

    public static void MapSeo(this IEndpointRouteBuilder app)
    {
        app.MapGet("/robots.txt", (HttpRequest req) =>
        {
            var baseUrl = $"{req.Scheme}://{req.Host}";
            var text = $"""
                User-agent: *
                Disallow: /Account/
                Disallow: /admin
                Disallow: /checkout
                Disallow: /cart
                Disallow: /my-orders
                Sitemap: {baseUrl}/sitemap.xml
                """;
            return Results.Text(text, "text/plain", Encoding.UTF8);
        }).CacheOutput(CachePolicy);

        app.MapGet("/sitemap.xml", async (HttpRequest req, ProductService productService, CancellationToken ct) =>
        {
            var baseUrl = $"{req.Scheme}://{req.Host}";
            var products = await productService.ListActiveAsync(ct);

            // Direto em UTF-8: com StringBuilder, o cabeçalho dizia "utf-16" e o arquivo saía em UTF-8
            using var stream = new MemoryStream();
            using (var xml = XmlWriter.Create(stream, new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) }))
            {
                xml.WriteStartElement("urlset", "http://www.sitemaps.org/schemas/sitemap/0.9");
                Url(xml, $"{baseUrl}/");
                foreach (var url in new[] { "returns-policy", "privacy-policy", "terms-of-use" })
                    Url(xml, $"{baseUrl}/{url}");
                foreach (var p in products)
                    Url(xml, $"{baseUrl}/{p.Url}");
                xml.WriteEndElement();
            }
            return Results.Bytes(stream.ToArray(), "application/xml; charset=utf-8");
        }).CacheOutput(CachePolicy);

        // Feed do Google Merchant Center (listagens gratuitas no Google Shopping)
        app.MapGet("/products.xml", async (HttpRequest req, ProductService productService, IOptions<ShopOptions> store, CancellationToken ct) =>
        {
            var baseUrl = $"{req.Scheme}://{req.Host}";
            var products = await productService.ListStorefrontAsync(ct);
            var xml = ProductFeed.Build(products.Where(ProductFeed.ShouldList), baseUrl, store.Value.Name);
            return Results.Text(xml, "application/xml", Encoding.UTF8);
        }).CacheOutput(CachePolicy);
    }

    private static void Url(XmlWriter xml, string loc)
    {
        xml.WriteStartElement("url");
        xml.WriteElementString("loc", loc);
        xml.WriteEndElement();
    }
}
