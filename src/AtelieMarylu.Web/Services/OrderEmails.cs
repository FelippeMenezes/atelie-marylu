using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AtelieMarylu.Domain.Orders;
using AtelieMarylu.Domain.Text;
using AtelieMarylu.Infrastructure.Data;
using AtelieMarylu.Web.Configuration;
using AtelieMarylu.Web.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AtelieMarylu.Web.Services;

// Grava na outbox os e-mails de pedido (AML-0041). Só ADICIONA linhas ao DbContext que o OrderService já está usando:
// quem chama grava tudo na mesma transação da mudança do pedido (SaveChangesAsync + Commit) e só depois chama Wake().
// Nada aqui fala com o provedor de e-mail; quem entrega é o EmailOutboxDispatcher, em segundo plano.
public sealed class OrderEmails(IOptions<ShopOptions> store, EmailOutboxSignal signal, TimeProvider clock, ILogger<OrderEmails> logger)
{
    // Enums como texto ("Paid", não 2): uma linha pendente continua legível mesmo se a ordem do enum mudar
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    // Quantos "pedido recebido" um mesmo destinatário recebe em 24 horas. Uma cliente de verdade raramente passa de
    // 2 ou 3 (o MaxPendingOrdersPerCustomer é 3, e às vezes ela cancela e refaz para trocar o frete); 5 dá folga sem
    // virar ferramenta de spam contra a caixa de alguém.
    public const int MaxOrderReceivedPerDay = 5;
    public static readonly TimeSpan OrderReceivedWindow = TimeSpan.FromHours(24);

    // Destinatário lido dentro da mesma transação (nome e e-mail atuais da conta). Para o e-mail segue só o primeiro
    // nome, e só se o nome passar na regra de CustomerText (AML-0042); senão, "Olá!". Bônus de minimização: a
    // outbox guarda menos dado pessoal.
    public sealed record Customer(string? Email, string? FullName)
    {
        public string? SafeName => CustomerText.SafeFirstNameOrNull(FullName);
    }

    // Acorda o worker. Chamar só DEPOIS do Commit: antes dele, o worker não enxerga as linhas novas.
    public void Wake() => signal.Wake();

    // "Recebemos o seu pedido": a confirmação imediata que o Decreto 7.962/2013 (art. 4º, III) pede.
    // A loja não exige confirmar o e-mail (AML-0027): sem limite, alguém poderia cadastrar o e-mail de outra pessoa
    // e repetir "criar pedido → cancelar" para lotar a caixa dela e gastar a cota diária do Resend.
    public async Task EnqueueOrderReceivedAsync(ApplicationDbContext db, Order order, Customer customer, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(customer.Email)) return;
        var now = clock.GetUtcNow().UtcDateTime;
        if (!await CanSendOrderReceivedAsync(db, customer.Email, now, ct))
        {
            logger.LogWarning("Limite de e-mails de pedido recebido atingido; o pedido {OrderId} fica sem esse aviso (a tela já confirma).", order.Id);
            return;
        }
        Add(db, OutboxEmailKinds.OrderReceived, customer.Email, LanguageOf(order), order.Id,
            OrderEmailData.From(order, customer.SafeName, now));
    }

    // "Pagamento confirmado". Sem limite: só acontece com dinheiro de verdade entrando (webhook ou registro manual).
    public void EnqueuePaymentConfirmed(ApplicationDbContext db, Order order, Customer customer, decimal amountPaid)
    {
        if (string.IsNullOrEmpty(customer.Email)) return;
        var now = clock.GetUtcNow().UtcDateTime;
        Add(db, OutboxEmailKinds.PaymentConfirmed, customer.Email, LanguageOf(order), order.Id,
            OrderEmailData.From(order, customer.SafeName, now, amountPaid));
    }

    // Solicitação de cancelamento (AML-0040), as mesmas regras de antes:
    // - Cliente: confirmação com protocolo, data e hora (Decreto 7.962/2013, art. 5º, §4º), no idioma do site.
    // - Loja: aviso para o Store:ContactEmail só com identificadores, no idioma padrão. O motivo da cliente nunca vai
    //   no e-mail: ele passaria pelo ImprovMX e ficaria no Gmail, onde a exclusão da conta não alcança.
    // O cancelamento de pedido não pago, feito na hora, não manda nada (mesmo risco de lotar a caixa de alguém).
    public void EnqueueCancellationRequest(ApplicationDbContext db, Order order, Customer customer, CancellationRequest request)
    {
        var data = new CancellationEmailData(order.Id, order.Code, customer.SafeName, request.Protocol, request.RequestedAt, request.OrderStatusAtRequest);
        // Idioma ATUAL do site, e não o do pedido (como nos outros e-mails), de propósito: é a confirmação de algo
        // que a cliente acabou de fazer nesta tela, e ela espera ler no idioma que está usando agora. Os outros
        // e-mails nascem de eventos fora da tela (webhook, painel), onde o único idioma conhecido é o do pedido.
        if (!string.IsNullOrEmpty(customer.Email))
            Add(db, OutboxEmailKinds.CancellationReceived, customer.Email, CurrentLanguage(), order.Id, data);

        var to = store.Value.ContactEmail;
        if (string.IsNullOrWhiteSpace(to))
        {
            logger.LogWarning("Store:ContactEmail está vazio: a solicitação de cancelamento do pedido {OrderId} aparece só no painel.", order.Id);
            return;
        }
        // Sem o nome: o aviso da loja leva só identificadores
        Add(db, OutboxEmailKinds.CancellationStaffNotice, to, SupportedLanguages.Default, order.Id, data with { CustomerName = null });
    }

    // Limite de "pedido recebido" por destinatário: true se ainda pode gravar mais um.
    // - Janela deslizante (últimas 24 h), não "desde a meia-noite": a fixa zera de uma vez e deixaria mandar o dobro
    //   em poucos minutos (5 às 23h59 e mais 5 às 00h00).
    // - Conta pelo destinatário, não pela conta: o abuso é contra a caixa de e-mail de alguém. Entram todas as linhas
    //   (pendentes, enviadas e abandonadas): o que se limita é a intenção de mandar, e uma falha não "devolve" a vaga.
    // - A contagem vem do banco (índice Recipient + Kind + CreatedAt), então sobrevive a reinícios e deploys.
    // - Conferir e depois gravar não é atômico: dois pedidos no mesmo instante podem passar juntos e mandar 6. Para um
    //   limite anti-spam isso basta; travar a linha da conta custaria uma trava a mais em toda criação de pedido.
    private async Task<bool> CanSendOrderReceivedAsync(ApplicationDbContext db, string recipient, DateTime now, CancellationToken ct)
    {
        var since = now - OrderReceivedWindow;
        var recent = await db.OutboxEmails.CountAsync(
            e => e.Recipient == recipient && e.Kind == OutboxEmailKinds.OrderReceived && e.CreatedAt > since, ct);
        return recent < MaxOrderReceivedPerDay;
    }

    private void Add<T>(ApplicationDbContext db, string kind, string recipient, string language, int orderId, T data)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        db.OutboxEmails.Add(new OutboxEmail
        {
            Kind = kind,
            Recipient = recipient,
            Language = language,
            OrderId = orderId,
            Payload = JsonSerializer.Serialize(data, Json),
            CreatedAt = now,
            NextAttemptAt = now,
        });
    }

    // O pedido guarda o idioma em que foi feito; um valor estranho (ou null, nos pedidos antigos) cai no padrão
    private static string LanguageOf(Order order) =>
        SupportedLanguages.IsSupported(order.Language) ? order.Language! : SupportedLanguages.Default;

    // Idioma de quem está usando o site agora (o circuito do Blazor guarda o do cookie)
    internal static string CurrentLanguage()
    {
        var name = CultureInfo.CurrentUICulture.Name;
        return SupportedLanguages.IsSupported(name) ? name : SupportedLanguages.Default;
    }
}
