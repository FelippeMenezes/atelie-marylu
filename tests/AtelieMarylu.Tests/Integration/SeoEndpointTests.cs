using System.Security.Claims;
using System.Text.Encodings.Web;
using AtelieMarylu.Domain.Catalog;
using AtelieMarylu.Web.Configuration;
using AtelieMarylu.Web.Endpoints;
using AtelieMarylu.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AtelieMarylu.Tests.Integration;

// robots, sitemap e feed chamados por HTTP, num servidor em memória: cabeçalho do XML e cache
public class SeoEndpointTests(TestDatabase database) : IClassFixture<TestDatabase>
{
    // Mesma ordem da loja (UseStorePipeline): login, autorização e só depois o cache
    private async Task<WebApplication> StartAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddRouting();
        builder.Services.AddAuthentication(HeaderAuthentication.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, HeaderAuthentication>(HeaderAuthentication.SchemeName, null);
        builder.Services.AddAuthorization();
        builder.Services.AddSeoOutputCache();
        builder.Services.AddOptions<ShopOptions>();
        builder.Services.AddSingleton(_ => new ProductService(database,
            new AuditService(new CurrentUserService(new ServiceCollection().BuildServiceProvider(), new HttpContextAccessor()), database)));

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseOutputCache();
        app.MapSeo();
        await app.StartAsync();
        return app;
    }

    private async Task<Product> ProductWithPhotoAsync(string name)
    {
        await using var db = database.CreateDbContext();
        var product = new Product { Name = name, Description = "Bolsa de tecido", Price = 80, Stock = 1 };
        product.Images.Add(new ProductImage { Url = "/uploads/foto.jpg", ThumbnailUrl = "/uploads/foto-p.jpg", SortOrder = 1 });
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product;
    }

    [FactWithDatabase]
    public async Task Feed_is_cached_so_repeated_requests_do_not_hit_the_database()
    {
        var product = await ProductWithPhotoAsync("Bolsa Cache Antes");
        await using var app = await StartAsync();
        var client = app.GetTestClient();

        var first = await client.GetStringAsync("/products.xml");

        // Muda o banco por fora: se a segunda resposta mudar, o cache não está funcionando
        await using (var db = database.CreateDbContext())
            await db.Products.Where(p => p.Id == product.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Name, "Bolsa Cache Depois"));
        var second = await client.GetStringAsync("/products.xml");

        Assert.Contains("Bolsa Cache Antes", first);
        Assert.Equal(first, second);
    }

    private async Task RenameAllProductsAsync(string name)
    {
        await using var db = database.CreateDbContext();
        await db.Products.ExecuteUpdateAsync(s => s.SetProperty(p => p.Name, name));
    }

    [FactWithDatabase]
    public async Task Query_string_does_not_bypass_the_cache()
    {
        await ProductWithPhotoAsync("Bolsa Query Antes");
        await using var app = await StartAsync();
        var client = app.GetTestClient();

        var first = await client.GetStringAsync("/products.xml");
        await RenameAllProductsAsync("Bolsa Query Depois");

        // O ataque: um parâmetro diferente a cada chamada. Antes da correção, cada um ia ao banco.
        foreach (var url in new[] { "/products.xml?r=1", "/products.xml?r=2&x=y", "/PRODUCTS.XML" })
            Assert.Equal(first, await client.GetStringAsync(url));
    }

    [FactWithDatabase]
    public async Task Signed_in_requests_never_get_or_fill_the_cache()
    {
        await ProductWithPhotoAsync("Bolsa Login Antes");
        await using var app = await StartAsync();
        var client = app.GetTestClient();

        await client.GetStringAsync("/products.xml");
        await RenameAllProductsAsync("Bolsa Login Depois");

        // Logado: vai ao banco (não recebe a cópia guardada)...
        using var signedIn = new HttpRequestMessage(HttpMethod.Get, "/products.xml");
        signedIn.Headers.Add(HeaderAuthentication.Header, "cliente-1");
        var response = await client.SendAsync(signedIn);
        Assert.Contains("Bolsa Login Depois", await response.Content.ReadAsStringAsync());

        // ...e a resposta dele não substitui a cópia dos anônimos
        Assert.Contains("Bolsa Login Antes", await client.GetStringAsync("/products.xml"));
    }

    [Fact]
    public void Store_pipeline_runs_the_cache_after_sign_in()
    {
        // O teste acima prova o comportamento com a ordem certa; este garante que a loja continua nessa ordem
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Join(folder.FullName, "AtelieMarylu.slnx")))
            folder = folder.Parent;
        var pipeline = File.ReadAllText(Path.Join(folder!.FullName, "src", "AtelieMarylu.Web", "Extensions", "WebApplicationExtensions.cs"));

        var cache = pipeline.IndexOf("app.UseOutputCache()", StringComparison.Ordinal);
        Assert.True(cache > 0, "UseOutputCache sumiu do pipeline");
        Assert.True(cache > pipeline.IndexOf("app.UseAuthentication()", StringComparison.Ordinal));
        Assert.True(cache > pipeline.IndexOf("app.UseAuthorization()", StringComparison.Ordinal));
    }

    [FactWithDatabase]
    public async Task Sitemap_and_feed_declare_the_encoding_they_are_sent_in()
    {
        await ProductWithPhotoAsync("Bolsa Acentuação");
        await using var app = await StartAsync();
        var client = app.GetTestClient();

        foreach (var url in new[] { "/sitemap.xml", "/products.xml" })
        {
            var response = await client.GetAsync(url);
            var bytes = await response.Content.ReadAsByteArrayAsync();
            var text = System.Text.Encoding.UTF8.GetString(bytes);

            Assert.Equal("utf-8", response.Content.Headers.ContentType!.CharSet);
            Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", text);
            // Sem BOM: alguns leitores de feed tropeçam nele
            Assert.NotEqual(0xEF, bytes[0]);
        }
    }
}

// Login de teste: quem manda o cabeçalho X-Test-User está logado com esse id
internal sealed class HeaderAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "TestHeader";
    public const string Header = "X-Test-User";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Header, out var user))
            return Task.FromResult(AuthenticateResult.NoResult());
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.ToString())], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
