using AtelieMarylu.Domain.Email;
using AtelieMarylu.Domain.Orders;
using AtelieMarylu.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AtelieMarylu.Web.Services;

// Entrega os e-mails da outbox (AML-0041). Acorda quando alguém grava um e-mail (EmailOutboxSignal) ou, no máximo,
// a cada PollInterval, e envia as linhas cuja hora chegou.
//
// Garantia: "ao menos uma vez". Se a loja cair depois de o Resend aceitar e antes de gravar SentAt, a linha é
// enviada de novo com a mesma Idempotency-Key, e o Resend não repete o e-mail (vale por 24 h).
//
// Um servidor só (como o OrderEvents e a fila de cobranças). Com mais de um, dois workers poderiam pegar a mesma
// linha; a chave de idempotência evita o e-mail duplicado, mas o certo seria reservar as linhas com
// SELECT ... FOR UPDATE SKIP LOCKED.
public class EmailOutboxDispatcher(
    IServiceScopeFactory scopes,
    EmailOutboxSignal signal,
    TimeProvider clock,
    ILogger<EmailOutboxDispatcher> logger) : BackgroundService
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    // Dado pessoal expira, o fato fica (veja o comentário de OutboxEmail):
    // - depois de 30 dias, a linha encerrada perde destinatário, payload e erro;
    // - depois de 5 anos (prazo do CDC, art. 27, para a cliente reclamar), a linha inteira sai.
    public static readonly TimeSpan PersonalDataRetention = TimeSpan.FromDays(30);
    public static readonly TimeSpan EvidenceRetention = TimeSpan.FromDays(5 * 365);

    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);
    public const int BatchSize = 20;
    private DateTime nextCleanup = DateTime.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Lote cheio: pode haver mais linhas vencidas, então roda de novo sem esperar
                    while (await DispatchDueAsync(stoppingToken) == BatchSize) { }
                    await CleanupIfDueAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Ex.: o banco caiu. Uma rodada com erro não pode derrubar o worker: tenta de novo na próxima.
                    logger.LogError(ex, "Falha na rodada da outbox de e-mails.");
                }
                await signal.WaitAsync(PollInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // A loja está desligando. Um envio interrompido no meio continua pendente e sai na próxima subida.
        }
    }

    // Uma rodada: envia até BatchSize linhas vencidas e devolve quantas leu. Público para os testes.
    public async Task<int> DispatchDueAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var services = scope.ServiceProvider;
        var renderer = services.GetRequiredService<OrderEmailRenderer>();
        var client = services.GetRequiredService<IEmailClient>();
        await using var db = await services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContextAsync(ct);

        var now = clock.GetUtcNow().UtcDateTime;
        var due = await db.OutboxEmails
            .Where(e => e.SentAt == null && e.FailedAt == null && e.SkippedAt == null && e.NextAttemptAt <= now)
            .OrderBy(e => e.NextAttemptAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        foreach (var email in due)
        {
            if (await SupersededReasonAsync(db, email, ct) is string reason)
            {
                email.SkippedAt = clock.GetUtcNow().UtcDateTime;
                email.LastError = reason;
                logger.LogInformation("E-mail {Kind} ({OutboxId}) do pedido {OrderId} não enviado: {Reason}.", email.Kind, email.Id, email.OrderId, reason);
            }
            else
            {
                await DeliverAsync(email, renderer, client, ct);
            }
            // Grava linha a linha: se a loja cair no meio do lote, só a linha da vez é enviada de novo.
            // CancellationToken.None: se o desligamento chegar depois de o Resend aceitar, o "enviado" ainda precisa
            // ser gravado (é rápido). Sem isso, a linha sairia de novo na próxima subida, dependendo só da
            // chave de idempotência para não duplicar.
            await db.SaveChangesAsync(CancellationToken.None);
        }
        return due.Count;
    }

    // Um e-mail que perdeu o sentido antes de sair (ex.: o Resend ficou fora do ar por uma hora) não é enviado.
    // "Pedido recebido" só vale enquanto o pedido aguarda pagamento: depois de pago, o "pagamento confirmado" já
    // traz o resumo (e chegaria fora de ordem); depois de cancelado ou expirado, diria "pague até" um prazo vencido.
    private static async Task<string?> SupersededReasonAsync(ApplicationDbContext db, OutboxEmail email, CancellationToken ct)
    {
        if (email.Kind != OutboxEmailKinds.OrderReceived || email.OrderId is not int orderId) return null;
        var status = await db.Orders.Where(o => o.Id == orderId).Select(o => (OrderStatus?)o.Status).FirstOrDefaultAsync(ct);
        return status == OrderStatus.AwaitingPayment ? null : $"Superseded: order is {status?.ToString() ?? "missing"}";
    }

    private async Task DeliverAsync(OutboxEmail email, OrderEmailRenderer renderer, IEmailClient client, CancellationToken ct)
    {
        try
        {
            var message = await renderer.RenderAsync(email);
            await client.SendAsync(message, ct);
            email.SentAt = clock.GetUtcNow().UtcDateTime;
            email.LastError = null;
        }
        catch (EmailDeliveryException ex)
        {
            RegisterFailure(email, ex.Message, ex.IsTransient, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Erro ao MONTAR o e-mail (payload inválido, tipo desconhecido, bug no componente). Repetir daria o mesmo
            // erro, e a linha travaria a fila para sempre ("poison message"): desiste dela e segue com as outras.
            RegisterFailure(email, $"{ex.GetType().Name}: {ex.Message}", transient: false, ex);
        }
    }

    private void RegisterFailure(OutboxEmail email, string error, bool transient, Exception ex)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        email.Attempts++;
        email.LastError = error.Length <= 500 ? error : error[..500];
        var next = transient ? OutboxRetryPolicy.NextAttemptAt(email.Attempts, now) : null;
        if (next is null)
        {
            email.FailedAt = now;
            // Sem o destinatário no log (dado pessoal): o Id da linha e o pedido bastam para achar no banco
            logger.LogError(ex, "ALERTA: o e-mail {Kind} ({OutboxId}) do pedido {OrderId} não foi entregue depois de {Attempts} tentativa(s); desisti.",
                email.Kind, email.Id, email.OrderId, email.Attempts);
            return;
        }
        email.NextAttemptAt = next.Value;
        logger.LogWarning(ex, "Falha ao entregar o e-mail {Kind} ({OutboxId}) do pedido {OrderId}; nova tentativa às {NextAttemptAt:u}.",
            email.Kind, email.Id, email.OrderId, email.NextAttemptAt);
    }

    // Limpeza das linhas encerradas (enviadas, abandonadas ou puladas), no máximo uma vez por hora. Devolve quantas
    // linhas mudaram (dados pessoais apagados + linhas removidas).
    public async Task<int> CleanupIfDueAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        if (now < nextCleanup) return 0;
        nextCleanup = now + CleanupInterval;

        using var scope = scopes.CreateScope();
        await using var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContextAsync(ct);

        // 1) Minimização: tira o dado pessoal e mantém o registro de que o e-mail saiu (ou de por que não saiu).
        //    O LastError sai junto: o motivo de um pulo não é dado pessoal, mas o de uma falha pode citar detalhes.
        var scrubbed = await Finished(db, now - PersonalDataRetention)
            .Where(e => e.Recipient != null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Recipient, (string?)null)
                .SetProperty(e => e.Payload, "{}")
                .SetProperty(e => e.LastError, (string?)null), ct);

        // 2) Fim da prova: depois do prazo do CDC, a linha inteira sai
        var deleted = await Finished(db, now - EvidenceRetention).ExecuteDeleteAsync(ct);
        return scrubbed + deleted;
    }

    // Linhas encerradas antes do limite
    private static IQueryable<OutboxEmail> Finished(ApplicationDbContext db, DateTime limit) =>
        db.OutboxEmails.Where(e => (e.SentAt != null && e.SentAt < limit) || (e.FailedAt != null && e.FailedAt < limit)
            || (e.SkippedAt != null && e.SkippedAt < limit));
}
