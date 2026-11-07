using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AtelieMarylu.Domain.Payments;
using AtelieMarylu.Web.Localization;

namespace AtelieMarylu.Tests;

public partial class LocalizationTests
{
    private static readonly string WebProject = FindWebProject();

    [GeneratedRegex("""\bL\["((?:[^"\\]|\\.)*)"[,\]]""")]
    private static partial Regex LocalizerCall();

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex Placeholder();

    private static string FindWebProject()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "AtelieMarylu.slnx")))
            folder = folder.Parent;
        return Path.Join(folder!.FullName, "src", "AtelieMarylu.Web");
    }

    private static Dictionary<string, string> ReadResource(string language) =>
        XDocument.Load(Path.Join(WebProject, "Resources", $"SharedResource.{language}.resx"))
            .Root!.Elements("data")
            .ToDictionary(d => d.Attribute("name")!.Value, d => d.Element("value")!.Value);

    [Theory]
    [InlineData("pt-BR")]
    [InlineData("es")]
    [InlineData("en")]
    public void Price_is_always_in_reais(string language)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(language);
        try
        {
            // O espaço entre "R$" e o valor é especial e muda com a versão do sistema
            var price = 39.90m.ToBrl();
            Assert.StartsWith("R$", price);
            Assert.EndsWith("39,90", price);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    // Texto novo na tela sem tradução apareceria em português para quem escolheu outro idioma
    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void Every_text_on_screen_has_translation(string language)
    {
        var resource = ReadResource(language);
        var missing = Directory.EnumerateFiles(Path.Join(WebProject, "Components"), "*.razor", SearchOption.AllDirectories)
            .SelectMany(file => LocalizerCall().Matches(File.ReadAllText(file)).Select(m => m.Groups[1].Value.Replace("\\\"", "\"")))
            .Distinct()
            .Where(key => !resource.ContainsKey(key))
            .ToList();

        Assert.True(missing.Count == 0, "Sem tradução: " + string.Join(" | ", missing));
    }

    [Fact]
    public void Languages_have_the_same_texts()
    {
        Assert.Equal(ReadResource("es").Keys.Order(), ReadResource("en").Keys.Order());
    }

    // Se uma tradução perder o {0}, o valor some da frase
    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void Translations_keep_the_placeholders(string language)
    {
        foreach (var (key, value) in ReadResource(language))
            Assert.Equal(Placeholder().Matches(key).Select(m => m.Value).Order(), Placeholder().Matches(value).Select(m => m.Value).Order());
    }

    [Theory]
    [InlineData("pt-BR", true)]
    [InlineData("es", true)]
    [InlineData("en", true)]
    [InlineData("fr", false)]
    [InlineData(null, false)]
    public void Only_supported_languages_are_accepted(string? code, bool expected)
    {
        Assert.Equal(expected, SupportedLanguages.IsSupported(code));
    }
}
