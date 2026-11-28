using AtelieMarylu.Web.Services;

namespace AtelieMarylu.Tests;

// Propriedades que qualquer política de nova tentativa da outbox precisa ter (AML-0041). Os números são decisão sua;
// estes testes só garantem que a regra não entra em laço nem desiste antes da hora.
public class OutboxRetryPolicyTests
{
    private static readonly DateTime Now = new(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void First_failure_tries_again_later()
    {
        var next = OutboxRetryPolicy.NextAttemptAt(1, Now);

        Assert.NotNull(next);
        Assert.True(next > Now);
    }

    [Fact]
    public void Waits_never_get_shorter()
    {
        var previous = TimeSpan.Zero;
        for (var attempts = 1; OutboxRetryPolicy.NextAttemptAt(attempts, Now) is DateTime next; attempts++)
        {
            Assert.True(next - Now >= previous, $"A espera depois de {attempts} falha(s) ficou menor que a anterior.");
            previous = next - Now;
        }
    }

    [Fact]
    public void Eventually_gives_up()
    {
        // Sem um fim, um e-mail que nunca sai ficaria tentando para sempre
        Assert.Contains(Enumerable.Range(1, 30), attempts => OutboxRetryPolicy.NextAttemptAt(attempts, Now) is null);
    }

    [Fact]
    public void Keeps_trying_for_more_than_a_day_to_survive_the_daily_quota()
    {
        // A cota do Resend é diária: desistir antes de 24 h perderia o e-mail de quem comprou depois que ela acabou
        var waited = TimeSpan.Zero;
        var attempts = 1;
        for (; OutboxRetryPolicy.NextAttemptAt(attempts, Now) is DateTime next; attempts++)
            waited += next - Now;

        Assert.InRange(waited, TimeSpan.FromHours(24), TimeSpan.FromHours(48));
        Assert.Equal(9, attempts); // 1 envio + 8 novas tentativas; a 9ª falha desiste
    }

    [Fact]
    public void First_retry_is_quick()
    {
        // A maioria das falhas é um soluço, e a cliente que acabou de comprar espera o e-mail logo
        Assert.Equal(Now.AddMinutes(1), OutboxRetryPolicy.NextAttemptAt(1, Now));
    }
}
