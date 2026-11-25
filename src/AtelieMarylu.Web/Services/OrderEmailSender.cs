using AtelieMarylu.Domain.Email;
using AtelieMarylu.Domain.Orders;
using AtelieMarylu.Infrastructure.Data;
using AtelieMarylu.Web.Components.Emails;
using AtelieMarylu.Web.Configuration;
using AtelieMarylu.Web.Localization;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace AtelieMarylu.Web.Services;

// E-mails do cancelamento pedido pela cliente (AML-0040). Segue o mesmo desenho do AccountEmailSender:
// componente Razor → HtmlRenderer → IEmailClient.
//
// - Cliente: confirmação imediata com protocolo, data e hora, como exige o Decreto 7.962/2013 (art. 5º, §4º).
//   Sai no idioma em que ela usa o site. Vale também para o pedido não pago, cancelado na hora.
// - Loja: aviso para o Store:ContactEmail, só quando há uma solicitação para a equipe responder.
//   Sai no idioma padrão da loja, não no da cliente.
// - A solicitação já está gravada quando os e-mails saem: uma falha de entrega fica só no log, e o
//   destaque no painel continua avisando a equipe.
public sealed class OrderEmailSender(
    IEmailClient client,
    IStringLocalizer<SharedResource> L,
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IOptions<ShopOptions> store,
    NavigationManager navigation,
    IServiceProvider services,
    ILoggerFactory loggerFactory,
    ILogger<OrderEmailSender> logger)
{
    public async Task TrySendCancellationEmailsAsync(CancellationOutcome outcome, CancellationToken ct = default)
    {
        await TrySendToCustomerAsync(outcome, ct);
        if (!outcome.CancelledNow) await TrySendToStoreAsync(outcome);
    }

    private async Task TrySendToCustomerAsync(CancellationOutcome outcome, CancellationToken ct)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var customer = await db.Users.AsNoTracking().Where(u => u.Id == outcome.CustomerId)
                .Select(u => new { u.Email, u.FullName }).FirstOrDefaultAsync(ct);
            if (string.IsNullOrEmpty(customer?.Email)) return;

            var html = await EmailHtml.RenderAsync<CancellationReceivedMessage>(services, loggerFactory, new()
            {
                [nameof(CancellationReceivedMessage.Name)] = customer.FullName,
                [nameof(CancellationReceivedMessage.OrderCode)] = outcome.OrderCode,
                [nameof(CancellationReceivedMessage.Protocol)] = outcome.Protocol,
                [nameof(CancellationReceivedMessage.ReceivedAt)] = outcome.At.FormatBrasilia(),
                [nameof(CancellationReceivedMessage.OrderUrl)] = navigation.ToAbsoluteUri($"my-orders/{outcome.OrderCode}").AbsoluteUri,
            });
            var subject = outcome.CancelledNow
                ? L["Order {0} cancelled · Ateliê Marylu", outcome.OrderCode]
                : L["Cancellation request received · Order {0}", outcome.OrderCode];
            await client.SendAsync(new EmailMessage(customer.Email, subject, html));
        }
        catch (EmailDeliveryException ex)
        {
            logger.LogError(ex, "Could not send the cancellation confirmation of order {OrderId}.", outcome.OrderId);
        }
    }

    private async Task TrySendToStoreAsync(CancellationOutcome outcome)
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
                [nameof(CancellationRequestStaffMessage.Reason)] = outcome.Reason,
                [nameof(CancellationRequestStaffMessage.AdminUrl)] = navigation.ToAbsoluteUri($"admin/orders/{outcome.OrderId}").AbsoluteUri,
            });
            await client.SendAsync(new EmailMessage(to, L["Cancellation request · Order {0}", outcome.OrderCode], html));
        }
        catch (EmailDeliveryException ex)
        {
            logger.LogError(ex, "Could not send the cancellation request notice of order {OrderId} to the store.", outcome.OrderId);
        }
    }
}
