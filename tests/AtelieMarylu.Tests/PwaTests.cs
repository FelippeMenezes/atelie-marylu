using System.Text.Json;
using System.Text.RegularExpressions;
using AtelieMarylu.Web.Security;

namespace AtelieMarylu.Tests;

// O PWA é feito de arquivos estáticos que nenhum outro teste olha. Um ícone apagado ou um JSON
// quebrado não dá erro no build: o navegador só para de oferecer "Instalar app", sem avisar ninguém.
public partial class PwaTests
{
    private static readonly string WwwRoot = FindWwwRoot();

    [GeneratedRegex(@"'(/[^']+)'")]
    private static partial Regex QuotedPath();

    private static string FindWwwRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "AtelieMarylu.slnx")))
            folder = folder.Parent;
        return Path.Join(folder!.FullName, "src", "AtelieMarylu.Web", "wwwroot");
    }

    private static string LocalPath(string url) => Path.Join(WwwRoot, url.TrimStart('/'));

    [Fact]
    public void Manifest_has_what_browsers_require_to_install()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(LocalPath("/manifest.webmanifest")));
        var root = manifest.RootElement;

        Assert.Equal("Marylu", root.GetProperty("short_name").GetString());
        Assert.Equal("/", root.GetProperty("start_url").GetString());
        Assert.Equal("standalone", root.GetProperty("display").GetString());

        var icons = root.GetProperty("icons").EnumerateArray().ToList();
        // O Chrome exige um ícone de 192 e um de 512; o maskable evita o logo cortado no Android
        Assert.Contains(icons, i => i.GetProperty("sizes").GetString() == "192x192");
        Assert.Contains(icons, i => i.GetProperty("sizes").GetString() == "512x512");
        Assert.Contains(icons, i => i.GetProperty("purpose").GetString() == "maskable");
        Assert.All(icons, i => Assert.True(File.Exists(LocalPath(i.GetProperty("src").GetString()!)),
            $"Ícone não encontrado: {i.GetProperty("src")}"));
    }

    // Um service worker de outro domínio poderia ler e alterar todas as páginas da loja
    [Fact]
    public void Csp_allows_worker_and_manifest_only_from_the_store()
    {
        var csp = SecurityHeaders.BuildCsp(false, turnstile: true, googleLogin: true);
        Assert.Contains("worker-src 'self';", csp);
        Assert.Contains("manifest-src 'self';", csp);
    }

    [Fact]
    public void Service_worker_precaches_only_files_that_exist()
    {
        // Se um arquivo da lista não existir, o cache.addAll falha e o service worker nem instala
        var script = File.ReadAllText(LocalPath("/service-worker.js"));
        var precache = Regex.Match(script, @"PRECACHE = \[(.*?)\]").Groups[1].Value;
        var paths = QuotedPath().Matches(precache).Select(m => m.Groups[1].Value)
            .Append("/offline.html").Distinct().ToList();

        Assert.NotEmpty(paths);
        Assert.All(paths, p => Assert.True(File.Exists(LocalPath(p)), $"Arquivo não encontrado: {p}"));
    }
}
