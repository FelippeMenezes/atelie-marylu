using System.Net;
using System.Text;
using AtelieMarylu.Infrastructure.Integrations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AtelieMarylu.Tests;

// Validação no servidor do Turnstile, com o Cloudflare substituído por respostas prontas
public class TurnstileTests
{
    [Fact]
    public async Task Accepts_when_cloudflare_confirms()
    {
        var created = Create("""{"success": true}""");
        using var turnstile = created.Item1;
        using var handler = created.Item2;
        Assert.True(await turnstile.ValidateAsync("token", "203.0.113.5"));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Refuses_when_cloudflare_refuses()
    {
        var created = Create("""{"success": false, "error-codes": ["timeout-or-duplicate"]}""");
        using var turnstile = created.Item1;
        using var handler = created.Item2;
        Assert.False(await turnstile.ValidateAsync("token", null));
    }

    [Fact]
    public async Task Refuses_missing_or_oversized_token_without_calling_cloudflare()
    {
        var created = Create("""{"success": true}""");
        using var turnstile = created.Item1;
        using var handler = created.Item2;
        Assert.False(await turnstile.ValidateAsync(null, null));
        Assert.False(await turnstile.ValidateAsync(new string('a', Turnstile.MaxTokenLength + 1), null));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Fails_closed_when_response_is_not_json()
    {
        // Ex.: uma página de erro HTML de algum proxy no caminho
        var created = Create("<html>502 Bad Gateway</html>", HttpStatusCode.BadGateway, "text/html");
        using var turnstile = created.Item1;
        using var handler = created.Item2;
        Assert.False(await turnstile.ValidateAsync("token", null));
    }

    [Fact]
    public async Task Is_disabled_without_keys()
    {
        var created = Create("""{"success": false}""", siteKey: "");
        using var turnstile = created.Item1;
        using var handler = created.Item2;
        Assert.False(turnstile.IsActive);
        Assert.True(await turnstile.ValidateAsync(null, null));
        Assert.Equal(0, handler.Calls);
    }

    private static (Turnstile, FakeHandler) Create(string body, HttpStatusCode status = HttpStatusCode.OK,
        string mediaType = "application/json", string siteKey = "site")
    {
        var handler = new FakeHandler(body, status, mediaType);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://challenges.cloudflare.com/") };
        var options = Options.Create(new TurnstileOptions { SiteKey = siteKey, SecretKey = "secret" });
        return (new Turnstile(http, options, NullLogger<Turnstile>.Instance), handler);
    }

    private sealed class FakeHandler(string body, HttpStatusCode status, string mediaType) : HttpMessageHandler, IDisposable
    {
        public int Calls { get; private set; }
        private bool _disposed;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            base.Dispose();
        }
    }
}
