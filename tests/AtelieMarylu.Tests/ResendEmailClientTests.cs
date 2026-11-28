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

    // A outbox tenta de novo só o que é passageiro (AML-0041). A pergunta é "de quem é o problema?": do e-mail
    // (definitivo) ou do provedor, do volume ou da configuração da loja (passageiro: alguém corrige e o e-mail sai)
    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, true)]     // limite por segundo ou cota do dia
    [InlineData(HttpStatusCode.Conflict, true)]            // outra requisição com a mesma chave em andamento
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.Unauthorized, true)]        // chave vencida ou trocada: a equipe corrige
    [InlineData(HttpStatusCode.Forbidden, true)]           // domínio sem verificação: a equipe corrige
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.UnprocessableEntity, false)] // dados inválidos (ex.: endereço malformado)
    [InlineData(HttpStatusCode.RequestEntityTooLarge, false)]
    public async Task Refusal_says_whether_a_retry_can_help(HttpStatusCode status, bool transient)
    {
        var client = Client(_ => new HttpResponseMessage(status) { Content = new StringContent("{}") });

        var ex = await Assert.ThrowsAsync<EmailDeliveryException>(() => client.SendAsync(Message));
        Assert.Equal(transient, ex.IsTransient);
    }

    [Fact]
    public async Task Key_already_used_with_other_content_means_already_delivered()
    {
        // O Resend não guarda requisições que falharam: se a chave existe, o envio anterior deu certo.
        // Tentar de novo até a chave expirar (24 h) mandaria o e-mail duas vezes.
        var client = Client(_ => Error(HttpStatusCode.Conflict, "invalid_idempotent_request"));

        await client.SendAsync(Message with { IdempotencyKey = "outbox/123" });
    }

    [Fact]
    public async Task Same_key_still_running_is_retried()
    {
        var client = Client(_ => Error(HttpStatusCode.Conflict, "concurrent_idempotent_requests"));

        var ex = await Assert.ThrowsAsync<EmailDeliveryException>(() => client.SendAsync(Message with { IdempotencyKey = "outbox/123" }));
        Assert.True(ex.IsTransient);
    }

    [Fact]
    public async Task Error_message_from_the_provider_stays_out_of_the_exception()
    {
        // A mensagem do provedor pode repetir o destinatário (dado pessoal); a exceção vai para o log e para o banco
        var client = Client(_ => Error(HttpStatusCode.UnprocessableEntity, "validation_error", "Invalid `to`: cliente@teste.local"));

        var ex = await Assert.ThrowsAsync<EmailDeliveryException>(() => client.SendAsync(Message));
        Assert.Contains("validation_error", ex.Message);
        Assert.DoesNotContain("cliente@teste.local", ex.Message);
    }

    [Fact]
    public async Task Odd_error_name_is_ignored()
    {
        // Só códigos simples (letras e "_") entram na mensagem
        var client = Client(_ => Error(HttpStatusCode.UnprocessableEntity, "<script>cliente@teste.local</script>"));

        var ex = await Assert.ThrowsAsync<EmailDeliveryException>(() => client.SendAsync(Message));
        Assert.DoesNotContain("cliente@teste.local", ex.Message);
    }

    private static HttpResponseMessage Error(HttpStatusCode status, string name, string message = "erro") => new(status)
    {
        Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(new { statusCode = (int)status, name, message })),
    };

    [Fact]
    public async Task Idempotency_key_goes_in_the_header()
    {
        HttpRequestMessage? sent = null;
        var client = Client(request => { sent = request; return new HttpResponseMessage(HttpStatusCode.OK); });

        await client.SendAsync(Message with { IdempotencyKey = "outbox/123" });

        Assert.Equal("outbox/123", Assert.Single(sent!.Headers.GetValues("Idempotency-Key")));
    }

    [Fact]
    public async Task Without_key_no_idempotency_header()
    {
        HttpRequestMessage? sent = null;
        var client = Client(request => { sent = request; return new HttpResponseMessage(HttpStatusCode.OK); });

        await client.SendAsync(Message);

        Assert.False(sent!.Headers.Contains("Idempotency-Key"));
    }

    [Fact]
    public async Task Unreachable_becomes_delivery_exception()
    {
        var client = Client(_ => throw new HttpRequestException("sem conexão"));

        var ex = await Assert.ThrowsAsync<EmailDeliveryException>(() => client.SendAsync(Message));
        Assert.IsType<HttpRequestException>(ex.InnerException);
        Assert.True(ex.IsTransient);
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
