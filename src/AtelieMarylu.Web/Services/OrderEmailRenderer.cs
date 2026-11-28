using System.Text.Json;
using AtelieMarylu.Domain.Email;
using AtelieMarylu.Domain.Orders;
using AtelieMarylu.Infrastructure.Data;
using AtelieMarylu.Web.Components.Emails;
using AtelieMarylu.Web.Configuration;
using AtelieMarylu.Web.Localization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace AtelieMarylu.Web.Services;

// Monta o e-mail de uma linha da outbox: componente Razor de Components/Emails → HtmlRenderer → EmailMessage.
// Roda no EmailOutboxDispatcher, fora de qualquer tela: o idioma vem da linha (OutboxEmail.Language) e os links,
// do Store:PublicBaseUrl (não há NavigationManager em segundo plano).
//
// O resultado precisa ser o MESMO a cada tentativa da mesma linha: o Resend compara o conteúdo junto com a chave de
// idempotência e responde 409 se mudar. Por isso tudo vem do payload gravado (datas incluídas), nada de "agora".
public sealed class OrderEmailRenderer(
    IStringLocalizer<SharedResource> L,
    IOptions<ShopOptions> store,
    IServiceProvider services,
    ILoggerFactory loggerFactory)
{
    public async Task<EmailMessage> RenderAsync(OutboxEmail email)
    {
        using var _ = EmailHtml.UseCulture(SupportedLanguages.IsSupported(email.Language) ? email.Language : SupportedLanguages.Default);
        var (subject, html) = email.Kind switch
        {
            OutboxEmailKinds.OrderReceived => await OrderReceivedAsync(Read<OrderEmailData>(email)),
            OutboxEmailKinds.PaymentConfirmed => await PaymentConfirmedAsync(Read<OrderEmailData>(email)),
            OutboxEmailKinds.CancellationReceived => await CancellationReceivedAsync(Read<CancellationEmailData>(email)),
            OutboxEmailKinds.CancellationStaffNotice => await CancellationStaffNoticeAsync(Read<CancellationEmailData>(email)),
            _ => throw new InvalidOperationException($"Tipo de e-mail desconhecido na outbox: {email.Kind}"),
        };
        // "outbox/<id>": uma chave por e-mail, a mesma em todas as tentativas (padrão recomendado pelo Resend)
        var to = email.Recipient ?? throw new InvalidOperationException($"Linha da outbox sem destinatário ({email.Id}).");
        return new EmailMessage(to, subject, html, $"outbox/{email.Id}");
    }

    private async Task<(string, string)> OrderReceivedAsync(OrderEmailData data) =>
        (L["Order {0} received", data.OrderCode], await RenderAsync<OrderReceivedMessage>(new()
        {
            [nameof(OrderReceivedMessage.Order)] = data,
            [nameof(OrderReceivedMessage.OrderUrl)] = store.Value.PublicUrl($"my-orders/{data.OrderCode}"),
        }));

    private async Task<(string, string)> PaymentConfirmedAsync(OrderEmailData data) =>
        (L["Payment confirmed · Order {0}", data.OrderCode], await RenderAsync<PaymentConfirmedMessage>(new()
        {
            [nameof(PaymentConfirmedMessage.Order)] = data,
            [nameof(PaymentConfirmedMessage.OrderUrl)] = store.Value.PublicUrl($"my-orders/{data.OrderCode}"),
        }));

    private async Task<(string, string)> CancellationReceivedAsync(CancellationEmailData data) =>
        (L["Cancellation request received · Order {0}", data.OrderCode], await RenderAsync<CancellationReceivedMessage>(new()
        {
            [nameof(CancellationReceivedMessage.Name)] = data.CustomerName,
            [nameof(CancellationReceivedMessage.OrderCode)] = data.OrderCode,
            [nameof(CancellationReceivedMessage.Protocol)] = data.Protocol,
            [nameof(CancellationReceivedMessage.ReceivedAt)] = data.At.FormatBrasilia(),
            [nameof(CancellationReceivedMessage.OrderUrl)] = store.Value.PublicUrl($"my-orders/{data.OrderCode}"),
        }));

    private async Task<(string, string)> CancellationStaffNoticeAsync(CancellationEmailData data) =>
        (L["Cancellation request · Order {0}", data.OrderCode], await RenderAsync<CancellationRequestStaffMessage>(new()
        {
            [nameof(CancellationRequestStaffMessage.OrderCode)] = data.OrderCode,
            [nameof(CancellationRequestStaffMessage.Protocol)] = data.Protocol,
            [nameof(CancellationRequestStaffMessage.ReceivedAt)] = data.At.FormatBrasilia(),
            [nameof(CancellationRequestStaffMessage.OrderStatus)] = Order.Describe(data.OrderStatusAtRequest),
            [nameof(CancellationRequestStaffMessage.AdminUrl)] = store.Value.PublicUrl($"admin/orders/{data.OrderId}"),
        }));

    private Task<string> RenderAsync<TComponent>(Dictionary<string, object?> parameters) where TComponent : IComponent =>
        EmailHtml.RenderAsync<TComponent>(services, loggerFactory, parameters);

    private static T Read<T>(OutboxEmail email) =>
        JsonSerializer.Deserialize<T>(email.Payload, OrderEmails.Json)
        ?? throw new InvalidOperationException($"Payload vazio na outbox ({email.Id}).");
}
