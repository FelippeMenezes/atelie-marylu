using System.Text.Json;
using System.Xml.Linq;
using AtelieMarylu.Domain.Catalog;
using AtelieMarylu.Web.Configuration;
using AtelieMarylu.Web.Seo;

namespace AtelieMarylu.Tests;

// Feed do Google Merchant Center, dados estruturados da loja e o link de avaliação
public class SeoTests
{
    private static readonly XNamespace G = "http://base.google.com/ns/1.0";

    private static Product Bag(string description = "Bolsa de tecido floral com alça de ombro") => new()
    {
        Id = 7,
        Name = "Bolsa Primavera",
        Description = description,
        Price = 89.9m,
        Stock = 2,
        Images =
        [
            new ProductImage { Url = "/uploads/b.jpg", SortOrder = 2 },
            new ProductImage { Url = "/uploads/a.jpg", SortOrder = 1 },
        ],
    };

    private static XElement OnlyItem(string xml) => XDocument.Parse(xml).Descendants("item").Single();

    [Fact]
    public void Feed_has_the_fields_google_requires()
    {
        var item = OnlyItem(ProductFeed.Build([Bag()], "https://www.ateliemarylu.com.br/", "Ateliê Marylu"));

        Assert.Equal("7", item.Element(G + "id")!.Value);
        Assert.Equal("Bolsa Primavera", item.Element(G + "title")!.Value);
        Assert.Equal("https://www.ateliemarylu.com.br/product/7/bolsa-primavera", item.Element(G + "link")!.Value);
        // A capa é a foto de menor SortOrder, e as outras vão como fotos adicionais
        Assert.Equal("https://www.ateliemarylu.com.br/uploads/a.jpg", item.Element(G + "image_link")!.Value);
        Assert.Equal("https://www.ateliemarylu.com.br/uploads/b.jpg", item.Element(G + "additional_image_link")!.Value);
        Assert.Equal("in_stock", item.Element(G + "availability")!.Value);
        Assert.Equal("new", item.Element(G + "condition")!.Value);
        Assert.Equal("Ateliê Marylu", item.Element(G + "brand")!.Value);
        Assert.Equal("no", item.Element(G + "identifier_exists")!.Value);
    }

    [Fact]
    public void Feed_price_uses_dot_and_currency_in_any_culture()
    {
        // Em pt-BR, ToString() daria "89,90": o Google recusaria o preço
        var previous = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("pt-BR");
        try
        {
            var item = OnlyItem(ProductFeed.Build([Bag()], "https://loja.test", "Loja"));
            Assert.Equal("89.90 BRL", item.Element(G + "price")!.Value);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Feed_marks_sold_out_and_falls_back_to_name_without_description()
    {
        var product = Bag(description: "");
        product.Stock = 0;

        var item = OnlyItem(ProductFeed.Build([product], "https://loja.test", "Loja"));

        Assert.Equal("out_of_stock", item.Element(G + "availability")!.Value);
        Assert.Equal("Bolsa Primavera", item.Element(G + "description")!.Value);
    }

    [Fact]
    public void Feed_escapes_special_characters_and_declares_utf8()
    {
        var product = Bag(description: "Alça <ajustável> & forro </item>");
        var xml = ProductFeed.Build([product], "https://loja.test", "Loja");

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", xml);
        Assert.Equal("Alça <ajustável> & forro </item>", OnlyItem(xml).Element(G + "description")!.Value);
    }

    [Fact]
    public void Feed_lists_active_product_with_photo_and_price_even_when_sold_out()
    {
        var product = Bag();
        Assert.True(ProductFeed.ShouldList(product));

        // Esgotado continua no feed (como out_of_stock), para não perder a revisão e o histórico no Google
        product.Stock = 0;
        Assert.True(ProductFeed.ShouldList(product));
    }

    [Fact]
    public void Feed_skips_products_google_would_reject()
    {
        var noPhoto = Bag();
        noPhoto.Images = [];
        var free = Bag();
        free.Price = 0;
        var inactive = Bag();
        inactive.IsActive = false;
        var noName = Bag();
        noName.Name = " ";

        Assert.False(ProductFeed.ShouldList(noPhoto));
        Assert.False(ProductFeed.ShouldList(free));
        Assert.False(ProductFeed.ShouldList(inactive));
        Assert.False(ProductFeed.ShouldList(noName));
    }

    // Regressões da revisão do AML-0034: um produto com texto "sujo" derrubava o feed inteiro (500)

    [Fact]
    public void Feed_survives_emoji_at_the_cut_point()
    {
        // O emoji ocupa as posições 146 e 147; o corte antigo (147) partia o par ao meio
        var product = Bag();
        product.Name = new string('a', 146) + "👜 bolsa";

        var title = OnlyItem(ProductFeed.Build([product], "https://loja.test", "Loja")).Element(G + "title")!.Value;

        Assert.Equal(new string('a', 146) + "...", title);
    }

    [Fact]
    public void Feed_removes_control_characters_pasted_from_other_apps()
    {
        var product = Bag(description: "Bolsa\vlinda\u0001 com\tforro");

        var description = OnlyItem(ProductFeed.Build([product], "https://loja.test", "Loja")).Element(G + "description")!.Value;

        Assert.Equal("Bolsalinda com\tforro", description);
    }

    [Fact]
    public void Feed_keeps_product_text_that_looks_like_the_xml_header()
    {
        // O remendo antigo (Replace de "utf-16") também reescrevia o texto do produto
        var product = Bag(description: "encoding=\"utf-16\"");

        var description = OnlyItem(ProductFeed.Build([product], "https://loja.test", "Loja")).Element(G + "description")!.Value;

        Assert.Equal("encoding=\"utf-16\"", description);
    }

    [Fact]
    public void Feed_skips_name_made_only_of_invisible_characters()
    {
        var product = Bag();
        product.Name = "\v\u0001";
        Assert.False(ProductFeed.ShouldList(product));
    }

    [Theory]
    [InlineData("abcdef", 10, "abcdef")]                       // cabe: não mexe
    [InlineData("abcdefghijkl", 10, "abcdefg...")]
    [InlineData("abcdef👍🏽xyz", 11, "abcdef...")]               // emoji com tom de pele é um caractere só (4 posições)
    [InlineData("abcde👨‍👩‍👧xyz", 12, "abcde...")]                // família (emojis unidos por ZWJ) não se separa
    public void Truncate_never_splits_what_the_person_sees_as_one_character(string text, int max, string expected)
    {
        Assert.Equal(expected, SafeText.Truncate(text, max));
    }

    [Fact]
    public void ForXml_drops_half_of_an_emoji_and_keeps_whole_ones()
    {
        Assert.Equal("a👜b", SafeText.ForXml("a👜b\uD83D"));
        Assert.Equal("ab", SafeText.ForXml("a\uDC5Cb"));
    }

    [Fact]
    public void Store_data_has_the_return_policy_fields_google_requires()
    {
        using var json = JsonDocument.Parse(StoreStructuredData.Build(new ShopOptions(), "https://loja.test"));
        var policy = json.RootElement.GetProperty("hasMerchantReturnPolicy");

        Assert.Equal("BR", policy.GetProperty("applicableCountry").GetString());
        Assert.Equal("https://schema.org/MerchantReturnFiniteReturnWindow", policy.GetProperty("returnPolicyCategory").GetString());
        // Direito de arrependimento (CDC, art. 49), como na página /returns-policy
        Assert.Equal(7, policy.GetProperty("merchantReturnDays").GetInt32());
        Assert.Equal("https://schema.org/FreeReturn", policy.GetProperty("returnFees").GetString());
    }

    [Fact]
    public void Store_data_skips_empty_fields_and_links_instagram()
    {
        var store = new ShopOptions { Name = "Ateliê Marylu", Phone = "", Instagram = "ateliemarylurj", ContactEmail = "contato@x.com" };

        using var json = JsonDocument.Parse(StoreStructuredData.Build(store, "https://www.ateliemarylu.com.br/"));
        var root = json.RootElement;

        Assert.Equal("OnlineStore", root.GetProperty("@type").GetString());
        Assert.Equal("https://www.ateliemarylu.com.br/", root.GetProperty("url").GetString());
        Assert.Equal("https://www.instagram.com/ateliemarylurj", root.GetProperty("sameAs")[0].GetString());
        Assert.False(root.TryGetProperty("telephone", out _));
        Assert.False(root.TryGetProperty("legalName", out _));
    }

    [Fact]
    public void Store_data_cannot_close_the_script_tag()
    {
        var store = new ShopOptions { Name = "</script><script>alert(1)</script>" };
        Assert.DoesNotContain("</script>", StoreStructuredData.Build(store, "https://loja.test"));
    }

    [Theory]
    [InlineData("https://g.page/r/abc/review", true)]
    [InlineData("", false)]
    [InlineData("g.page/r/abc/review", false)]          // sem https:// viraria link relativo dentro da loja
    [InlineData("http://g.page/r/abc/review", false)]
    [InlineData("javascript:alert(1)", false)]
    public void Review_link_only_accepts_https(string url, bool expected)
    {
        Assert.Equal(expected, new ShopOptions { GoogleReviewUrl = url }.HasReviewLink);
    }
}
