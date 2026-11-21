using System.Net;
using AtelieMarylu.Infrastructure.Integrations;
using Microsoft.Extensions.Logging;

namespace AtelieMarylu.Tests;

public sealed class PostalCodeLookupTests : IDisposable
{
    private readonly List<HttpClient> clients = [];
    private readonly WarningCounter logger = new();

    [Fact]
    public async Task Fills_the_address()
    {
        var lookup = Lookup("""{ "cep": "22452-694", "logradouro": "Avenida Presidente João Goulart", "bairro": "Vidigal", "localidade": "Rio de Janeiro", "uf": "RJ" }""");

        var address = await lookup.FindAsync("22452-694");

        Assert.NotNull(address);
        Assert.Equal("Rio de Janeiro", address.City);
        Assert.Equal("RJ", address.State);
    }

    [Theory]
    [InlineData("""{ "erro": "true" }""")] // formato atual do ViaCEP (21/11/2026)
    [InlineData("""{ "erro": true }""")]   // formato antigo
    public async Task Unknown_postal_code_is_not_treated_as_viacep_down(string body)
    {
        var address = await Lookup(body).FindAsync("99999999");

        Assert.Null(address);
        Assert.Equal(0, logger.Warnings); // antes, o "true" em texto quebrava a leitura e virava "ViaCEP indisponível"
    }

    private PostalCodeLookup Lookup(string body)
    {
        var http = new HttpClient(new FakeHandler(body)) { BaseAddress = new Uri("https://viacep.test/") };
        clients.Add(http);
        return new PostalCodeLookup(http, logger);
    }

    public void Dispose()
    {
        foreach (var http in clients)
            http.Dispose();
    }

    private sealed class FakeHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
    }

    private sealed class WarningCounter : ILogger<PostalCodeLookup>
    {
        public int Warnings { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning) Warnings++;
        }
    }
}
