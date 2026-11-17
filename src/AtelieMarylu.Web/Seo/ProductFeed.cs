using System.Globalization;
using System.Text;
using System.Xml;
using AtelieMarylu.Domain.Catalog;

namespace AtelieMarylu.Web.Seo;

// Feed de produtos para o Google Merchant Center (listagens gratuitas na aba Shopping).
// Formato RSS 2.0 com o namespace "g:" do Google: https://support.google.com/merchants/answer/7052112
// O Merchant Center busca este endereço sozinho, no horário configurado lá (ex.: uma vez por dia).
public static class ProductFeed
{
    private const string GoogleNamespace = "http://base.google.com/ns/1.0";

    // Limites do Google: título até 150 caracteres e descrição até 5000
    private const int MaxTitle = 150;
    private const int MaxDescription = 5000;

    // Decide se um produto ativo entra no feed. O Merchant Center reprova (e, se for recorrente,
    // suspende a conta) itens que quebram as regras dele, então este filtro protege a conta inteira.
    // Regras: https://support.google.com/merchants/answer/6324350 (imagem) e /answer/6324371 (preço).
    // Produto esgotado continua no feed, como "out_of_stock": é o que o Google recomenda. Tirar e pôr de volta
    // faz o item passar de novo pela revisão (até 3 dias) e perder o histórico de cliques. Do mesmo jeito,
    // a vitrine mostra o esgotado como "esgotado", em vez de esconder.
    public static bool ShouldList(Product product) =>
        product.IsActive
        // Sem foto, o Google reprova. O logo da loja no lugar conta como "imagem genérica", que também é proibido.
        && product.Cover is not null
        // Preço zero ou negativo é reprovado ("preço inválido") e seria uma oferta enganosa
        && product.Price > 0
        // Nome só com caracteres invisíveis ficaria vazio no XML depois da limpeza
        && !string.IsNullOrWhiteSpace(SafeText.ForXml(product.Name));

    // Monta o XML a partir de produtos já filtrados (com as imagens carregadas)
    public static string Build(IEnumerable<Product> products, string baseUrl, string storeName)
    {
        baseUrl = baseUrl.TrimEnd('/');
        // Escreve direto em UTF-8 (sem BOM): o cabeçalho sai com encoding="utf-8" sem precisar de remendo
        var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false) };
        using var stream = new MemoryStream();

        using (var xml = XmlWriter.Create(stream, settings))
        {
            xml.WriteStartElement("rss");
            xml.WriteAttributeString("version", "2.0");
            xml.WriteAttributeString("xmlns", "g", null, GoogleNamespace);
            xml.WriteStartElement("channel");
            xml.WriteElementString("title", SafeText.ForXml(storeName));
            xml.WriteElementString("link", $"{baseUrl}/");
            xml.WriteElementString("description", SafeText.ForXml(storeName));

            foreach (var p in products)
            {
                // Limpa antes de cortar: o corte conta o texto que realmente vai para o XML
                var name = SafeText.ForXml(p.Name);
                var description = SafeText.ForXml(p.Description);

                xml.WriteStartElement("item");
                Google(xml, "id", p.Id.ToString(CultureInfo.InvariantCulture));
                Google(xml, "title", SafeText.Truncate(name, MaxTitle));
                // Sem descrição o Google reprova o item; o nome é o mínimo aceitável
                Google(xml, "description", SafeText.Truncate(string.IsNullOrWhiteSpace(description) ? name : description, MaxDescription));
                Google(xml, "link", $"{baseUrl}/{p.Url}");
                if (p.Cover is { } cover)
                    Google(xml, "image_link", $"{baseUrl}{cover.Url}");
                foreach (var extra in p.Images.OrderBy(i => i.SortOrder).Skip(1).Take(10))
                    Google(xml, "additional_image_link", $"{baseUrl}{extra.Url}");
                Google(xml, "availability", p.IsAvailableForSale ? "in_stock" : "out_of_stock");
                // O Google exige ponto como separador e a moeda no fim: "89.90 BRL"
                Google(xml, "price", $"{p.Price.ToString("0.00", CultureInfo.InvariantCulture)} BRL");
                Google(xml, "condition", "new");
                Google(xml, "brand", SafeText.ForXml(storeName));
                // Peça artesanal não tem código de barras (GTIN) nem número de peça do fabricante
                Google(xml, "identifier_exists", "no");
                xml.WriteEndElement();
            }

            xml.WriteEndElement();
            xml.WriteEndElement();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void Google(XmlWriter xml, string name, string value) =>
        xml.WriteElementString("g", name, GoogleNamespace, value);
}
