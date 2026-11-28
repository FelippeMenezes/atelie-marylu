using System.Collections.Immutable;

namespace AtelieMarylu.Web.Services;

// Quando tentar de novo um e-mail da outbox que falhou por um motivo passageiro (EmailDeliveryException.IsTransient):
// Resend fora do ar, limite por segundo (429) ou cota do dia esgotada (também 429).
public static class OutboxRetryPolicy
{
    // Espera crescente ("exponential backoff"), em tabela em vez de fórmula: fica legível e cada degrau é uma decisão.
    // - Começa curto (1 e 5 min): a maioria das falhas é um soluço (rede, 5xx, limite por segundo) e a cliente que
    //   acabou de comprar espera o e-mail logo.
    // - Vai alongando até 12 h: numa queda longa, não martela o Resend nem enche o log.
    // - Soma cerca de 34 h antes de desistir. A cota do Resend é diária: se ela acabar de manhã, a tentativa do dia
    //   seguinte ainda pega o e-mail. Mais que isso, o aviso já perdeu o sentido e a equipe resolve pelo ALERTA.
    // Sem "jitter" (espera sorteada): ele evita que milhares de clientes repitam no mesmo segundo depois de uma queda.
    // Aqui há um worker só, que envia em sequência, então não há essa manada; e a tabela fixa deixa os testes exatos.
    // ImmutableArray: um "static readonly" de array protege só a referência; o conteúdo poderia ser trocado
    // (Waits[0] = TimeSpan.Zero) por qualquer código do projeto.
    private static readonly ImmutableArray<TimeSpan> Waits =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(3),
        TimeSpan.FromHours(6),
        TimeSpan.FromHours(12),
        TimeSpan.FromHours(12),
    ];

    // attempts = quantas tentativas já FALHARAM, contando a que acabou de falhar (1 na primeira falha).
    // Devolve a hora da próxima tentativa, ou null para desistir (a linha vira FailedAt e o log registra um ALERTA).
    public static DateTime? NextAttemptAt(int attempts, DateTime now) =>
        attempts >= 1 && attempts <= Waits.Length ? now + Waits[attempts - 1] : null;
}
