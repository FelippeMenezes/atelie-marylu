using AtelieMarylu.Domain.Payments;
using AtelieMarylu.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AtelieMarylu.Tests;

// A fila que fecha cobranças no provedor em segundo plano: tenta de novo, desiste com alerta e nunca derruba a loja
public class ChargeClosingServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 15, 0, 0, TimeSpan.Zero);

    private sealed class FlakyGateway(int failures) : IPaymentGateway
    {
        public int Calls { get; private set; }
        public string Name => "flaky";
        public bool VerifiesNotifications => true;

        public Task CancelChargeAsync(string? providerReference, CancellationToken ct = default)
        {
            Calls++;
            return Calls <= failures ? throw new PaymentGatewayException("fora do ar") : Task.CompletedTask;
        }

        public Task<PaymentStart> StartAsync(ChargeRequest charge, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PaymentNotification?> QueryStatusAsync(string externalId, string? providerReference, CancellationToken ct = default) => throw new NotSupportedException();
        public string? ClaimedExternalId(PaymentWebhookRequest request) => null;
        public Task<PaymentNotification?> ReadNotificationAsync(PaymentWebhookRequest request, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static ChargeClosingService Service(IPaymentGateway gateway) =>
        new(new ChargeClosingQueue(NullLogger<ChargeClosingQueue>.Instance),
            new ServiceCollection().AddSingleton(gateway).BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new FixedClock(Now), NullLogger<ChargeClosingService>.Instance)
        {
            RetryDelays = [TimeSpan.Zero, TimeSpan.Zero],
        };

    private static ChargeToClose Charge(DateTime? payBy = null) => new("CHEC_1", 1, payBy ?? Now.UtcDateTime.AddMinutes(30));

    [Fact]
    public async Task Tries_again_until_the_provider_answers()
    {
        var gateway = new FlakyGateway(failures: 2);

        await Service(gateway).CloseAsync(Charge(), CancellationToken.None);

        Assert.Equal(3, gateway.Calls);
    }

    [Fact]
    public async Task Gives_up_without_throwing_after_the_last_attempt()
    {
        // Falha permanente vira alerta no log; a fila segue para a próxima cobrança
        var gateway = new FlakyGateway(failures: 10);

        await Service(gateway).CloseAsync(Charge(), CancellationToken.None);

        Assert.Equal(3, gateway.Calls);
    }

    [Fact]
    public async Task Shutting_down_is_not_an_error()
    {
        // Desligar a loja cancela a espera da fila: o serviço tem de terminar normalmente, sem exceção
        var service = Service(new FlakyGateway(failures: 0));

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        Assert.True(service.ExecuteTask!.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Skips_charges_that_already_expired()
    {
        var gateway = new FlakyGateway(failures: 0);

        await Service(gateway).CloseAsync(Charge(payBy: Now.UtcDateTime.AddMinutes(-1)), CancellationToken.None);

        Assert.Equal(0, gateway.Calls);
    }
}
