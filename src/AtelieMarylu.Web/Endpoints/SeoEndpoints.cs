using System.Text;
using System.Xml;
using AtelieMarylu.Web.Services;

namespace AtelieMarylu.Web.Endpoints;

// robots.txt e sitemap.xml: dizem ao Google o que indexar (vitrine e produtos) e o que não (conta, painel, checkout)
public static class SeoEndpoints
{
    public static void MapSeo(this WebApplication app)
    {
        app.MapGet("/robots.txt", (HttpRequest req) =>
        {
            var baseUrl = $"{req.Scheme}://{req.Host}";
            var text = $"""
                User-agent: *
                Disallow: /Account/
                Disallow: /painel
                Disallow: /checkout
                Disallow: /cart
                Disallow: /meus-pedidos
                Sitemap: {baseUrl}/sitemap.xml
                """;
            return Results.Text(text, "text/plain", Encoding.UTF8);
        });

        app.MapGet("/sitemap.xml", async (HttpRequest req, ProductService productService, CancellationToken ct) =>
        {
            var baseUrl = $"{req.Scheme}://{req.Host}";
            var products = await productService.ListActiveAsync(ct);

            var sb = new StringBuilder();
            using (var xml = XmlWriter.Create(sb, new XmlWriterSettings { Indent = true, Async = true }))
            {
                xml.WriteStartElement("urlset", "http://www.sitemaps.org/schemas/sitemap/0.9");
                Url(xml, $"{baseUrl}/");
                foreach (var url in new[] { "trocas-e-devolucoes", "politica-de-privacidade", "termos-de-uso" })
                    Url(xml, $"{baseUrl}/{url}");
                foreach (var p in products)
                    Url(xml, $"{baseUrl}/{p.Url}");
                xml.WriteEndElement();
            }
            return Results.Text(sb.ToString(), "application/xml", Encoding.UTF8);
        });
    }

    private static void Url(XmlWriter xml, string loc)
    {
        xml.WriteStartElement("url");
        xml.WriteElementString("loc", loc);
        xml.WriteEndElement();
    }
}
