using System.Text;
using System.Xml;
using AtelieMarylu.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace AtelieMarylu.Web.Integracoes;

// robots.txt e sitemap.xml: dizem ao Google o que indexar (vitrine e produtos) e o que não (conta, painel, checkout)
public static class Seo
{
    public static void MapSeo(this WebApplication app)
    {
        app.MapGet("/robots.txt", (HttpRequest req) =>
        {
            var baseUrl = $"{req.Scheme}://{req.Host}";
            var texto = $"""
                User-agent: *
                Disallow: /Account/
                Disallow: /painel
                Disallow: /checkout
                Disallow: /carrinho
                Disallow: /meus-pedidos
                Sitemap: {baseUrl}/sitemap.xml
                """;
            return Results.Text(texto, "text/plain", Encoding.UTF8);
        });

        app.MapGet("/sitemap.xml", async (HttpRequest req, IDbContextFactory<ApplicationDbContext> dbFactory, CancellationToken ct) =>
        {
            var baseUrl = $"{req.Scheme}://{req.Host}";
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var produtos = await db.Produtos.AsNoTracking().Where(p => p.Ativo).ToListAsync(ct);

            var sb = new StringBuilder();
            using (var xml = XmlWriter.Create(sb, new XmlWriterSettings { Indent = true, Async = true }))
            {
                xml.WriteStartElement("urlset", "http://www.sitemaps.org/schemas/sitemap/0.9");
                Url(xml, $"{baseUrl}/");
                foreach (var url in new[] { "trocas-e-devolucoes", "politica-de-privacidade", "termos-de-uso" })
                    Url(xml, $"{baseUrl}/{url}");
                foreach (var p in produtos)
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
