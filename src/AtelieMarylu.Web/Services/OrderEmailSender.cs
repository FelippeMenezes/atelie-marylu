using AtelieMarylu.Domain.Email;
using AtelieMarylu.Domain.Orders;
using AtelieMarylu.Web.Components.Emails;
using AtelieMarylu.Web.Configuration;
using AtelieMarylu.Web.Localization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace AtelieMarylu.Web.Services;

// E-mails do cancelamento pedido pela cliente (AML-0040). Segue o mesmo desenho do AccountEmailSender:
// componente Razor → HtmlRenderer → IEmailClient.
//
// - Só a solicitação (pedido pago) gera e-mail. O cancelamento de pedido não pago, feito na hora, não manda nada:
//   a loja não exige confirmar o e-mail (AML-0027), e um e-mail por cancelamento permitiria cadastrar o e-mail
//   de outra pessoa e repetir "criar pedido → cancelar" para lotar a caixa dela e gastar a cota diária do Resend
//   (que também entrega o "esqueci minha senha"). A tela já confirma, e não há dinheiro para devolver.
// - Cliente: confirmação imediata com protocolo, data e hora, como exige o Decreto 7.962/2013 (art. 5º, §4º).
//   Sai no idioma em que ela usa o site. Repetir exige a equipe responder antes (uma solicitação aberta por vez).
// - Loja: aviso para o Store:ContactEmail, só com identificadores (código, protocolo, situação e link do painel).
//   O motivo da cliente nunca vai no e-mail: ele passa pelo ImprovMX e fica no Gmail, onde a exclusão da conta não
//   alcança (minimização, LGPD art. 6º, III). Sai no idioma padrão da loja, não no da cliente.
// - A solicitação já está gravada quando os e-mails saem: uma falha de entrega fica só no log, e o
//   destaque no painel continua avisando a equipe. Nome e e-mail vêm no CancellationOutcome, sem consultar o banco.
public sealed class OrderEmailSender(
    IEmailClient client,
    IStringLocalizer<SharedResource> L,
    IOptions<ShopOptions> store,
    NavigationManager navigation,
    IServiceProvider services,
    ILoggerFactory loggerFactory,
    ILogger<OrderEmailSender> logger)
{
    public async Task TrySendCancellationEmailsAsync(CancellationOutcome outcome, CancellationToken ct = default)
    {
        if (outcome.CancelledNow || outcome.Protocol is null) return;
        await TrySendToCustomerAsync(outcome, ct);
        await TrySendToStoreAsync(outcome, ct);
    }

    private async Task TrySendToCustomerAsync(CancellationOutcome outcome, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(outcome.CustomerEmail)) return;
        try
        {
            var html = await EmailHtml.RenderAsync<CancellationReceivedMessage>(services, loggerFactory, new()
            {
                [nameof(CancellationReceivedMessage.Name)] = outcome.CustomerName,
                [nameof(CancellationReceivedMessage.OrderCode)] = outcome.OrderCode,
                [nameof(CancellationReceivedMessage.Protocol)] = outcome.Protocol,
                [nameof(CancellationReceivedMessage.ReceivedAt)] = outcome.At.FormatBrasilia(),
                [nameof(CancellationReceivedMessage.OrderUrl)] = navigation.ToAbsoluteUri($"my-orders/{outcome.OrderCode}").AbsoluteUri,
            });
            await client.SendAsync(new EmailMessage(outcome.CustomerEmail,
                L["Cancellation request received · Order {0}", outcome.OrderCode], html), ct);
        }
        catch (EmailDeliveryException ex)
        {
            logger.LogError(ex, "Could not send the cancellation confirmation of order {OrderId}.", outcome.OrderId);
        }
    }

    private async Task TrySendToStoreAsync(CancellationOutcome outcome, CancellationToken ct)
    {
        var to = store.Value.ContactEmail;
        if (string.IsNullOrWhiteSpace(to))
        {
            logger.LogWarning("Store:ContactEmail is empty: the cancellation request of order {OrderId} is only on the admin panel.", outcome.OrderId);
            return;
        }

        try
        {
            using var _ = EmailHtml.UseCulture(SupportedLanguages.Default);
            var html = await EmailHtml.RenderAsync<CancellationRequestStaffMessage>(services, loggerFactory, new()
            {
                [nameof(CancellationRequestStaffMessage.OrderCode)] = outcome.OrderCode,
                [nameof(CancellationRequestStaffMessage.Protocol)] = outcome.Protocol,
                [nameof(CancellationRequestStaffMessage.ReceivedAt)] = outcome.At.FormatBrasilia(),
                [nameof(CancellationRequestStaffMessage.OrderStatus)] = Order.Describe(outcome.OrderStatusAtRequest),
                [nameof(CancellationRequestStaffMessage.AdminUrl)] = navigation.ToAbsoluteUri($"admin/orders/{outcome.OrderId}").AbsoluteUri,
            });
            await client.SendAsync(new EmailMessage(to, L["Cancellation request · Order {0}", outcome.OrderCode], html), ct);
        }
        catch (EmailDeliveryException ex)
        {
            logger.LogError(ex, "Could not send the cancellation request notice of order {OrderId} to the store.", outcome.OrderId);
        }
    }
}
