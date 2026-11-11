using System.Net;
using AtelieMarylu.Domain.Email;
using AtelieMarylu.Infrastructure.Integrations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AtelieMarylu.Tests;

// Toda falha de entrega do Resend vira EmailDeliveryException, que é a única exceção que as telas tratam.
// Se alguma falha escapar com outro tipo, o "esqueci minha senha" mostraria erro só para e-mails que
// têm conta (revelando quem é cliente) e o cadastro quebraria depois de criar a conta.
public sealed class ResendEmailClientTests : IDisposable
{
    // O xUnit cria uma instância da classe por teste e chama Dispose no fim: os HttpClient criados são descartados
    private readonly List<HttpClient> clients = [];

    private static readonly EmailMessage Message = new("cliente@teste.local", "Assunto", "<p>Olá</p>");

    [Fact]
    public async Task Success_does_not_throw()
    {
        var client = Client(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"id\":\"1\"}") });

        await client.SendAsync(Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]        // chave errada ou apagada
    [InlineData(HttpStatusCode.Forbidden)]           // domínio não verificado
    [InlineData(HttpStatusCode.TooManyRequests)]     // cota estourada
    [InlineData(HttpStatusCode.InternalServerError)] // Resend com problema
    public async Task Refusal_becomes_delivery_exception(HttpStatusCode status)
    {
        var client = Client(_ => new HttpResponseMessage(status) { Content = new StringContent("{\"message\":\"erro\"}") });

        await Assert.ThrowsAsync<EmailDeliveryException>(() => client.SendAsync(Message));
    }

    [Fact]
    public async Task Unreachable_becomes_delivery_exception()
    {
        var client = Client(_ => throw new HttpRequestException("sem conexão"));

        var ex = await Assert.ThrowsAsync<EmailDeliveryException>(() => client.SendAsync(Message));
        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    [Fact]
    public async Task Timeout_becomes_delivery_exception()
    {
        // É assim que o HttpClient sinaliza que passou do Timeout
        var client = Client(_ => throw new TaskCanceledException("timeout"));

        await Assert.ThrowsAsync<EmailDeliveryException>(() => client.SendAsync(Message));
    }

    [Fact]
    public async Task Cancellation_by_the_caller_is_not_hidden()
    {
        // Cancelamento pedido por quem chamou (ex.: o navegador fechou) não é falha do Resend
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var client = Client(_ => throw new TaskCanceledException("cancelado"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SendAsync(Message, cts.Token));
    }

    private ResendEmailClient Client(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var http = new HttpClient(new FakeHandler(respond)) { BaseAddress = new Uri("https://api.resend.test/") };
        clients.Add(http);
        return new ResendEmailClient(http,
            Options.Create(new ResendOptions { ApiKey = "re_teste", From = "Loja <loja@teste.local>" }),
            NullLogger<ResendEmailClient>.Instance);
    }

    public void Dispose()
    {
        foreach (var http in clients)
            http.Dispose();
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(respond(request));
    }
}
