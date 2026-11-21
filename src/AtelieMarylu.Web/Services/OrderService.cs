using AtelieMarylu.Domain.Auditing;
using AtelieMarylu.Domain.Carts;
using AtelieMarylu.Domain.Catalog;
using AtelieMarylu.Domain.Shipping;
using AtelieMarylu.Domain.Payments;
using AtelieMarylu.Domain.Orders;
using AtelieMarylu.Web.Configuration;
using AtelieMarylu.Web.Localization;
using AtelieMarylu.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AtelieMarylu.Web.Services;

// Orquestra pedido + estoque + frete + pagamento. As regras de cada pedido ficam na
// entidade Pedido (projeto Domain); aqui fica o que depende de banco e de serviços externos.
public class OrderService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IShippingCalculator shipping,
    IPaymentGateway gateway,
    IChargeCloser chargeCloser,
    AuditService audit,
    OrderEvents events,
    IOptions<ShopOptions> store,
    TimeProvider clock,
    IStringLocalizer<SharedResource> localizer,
    ILogger<OrderService> logger)
{
    public const string ManualProvider = "manual";
    private const int ListLimit = 200;
    private const string ConcurrencyMessage = "O pedido foi alterado por outra pessoa ao mesmo tempo. Recarregue a página e tente de novo.";

    public async Task<Order?> GetAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Orders.AsNoTracking().Include(p => p.Items).Include(p => p.Payments).FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    // O filtro por CustomerId garante que ninguém veja o pedido de outra pessoa trocando o código na URL
    public async Task<Order?> GetForCustomerAsync(string code, string? customerId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Orders.AsNoTracking().Include(p => p.Items).Include(p => p.Payments)
            .FirstOrDefaultAsync(p => p.Code == code && p.CustomerId == customerId, ct);
    }

    public async Task<List<Order>> ListForCustomerAsync(string? customerId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Orders.AsNoTracking().Include(p => p.Items)
            .Where(p => p.CustomerId == customerId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<List<Order>> ListAsync(OrderStatus? status, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.Orders.AsNoTracking().Include(p => p.Items).AsQueryable();
        if (status is not null)
            query = query.Where(p => p.Status == status);
        return await query.OrderByDescending(p => p.CreatedAt).Take(ListLimit).ToListAsync(ct);
    }

    // Endereço do último pedido, para sugerir no checkout
    public async Task<ShippingAddress?> GetLastAddressAsync(string customerId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Orders.AsNoTracking()
            .Where(p => p.CustomerId == customerId)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => p.Address)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<ShippingOption>> QuoteShippingAsync(string postalCode, IReadOnlyList<CartItem> items, CancellationToken ct = default)
    {
        Dictionary<int, Product> products;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            products = await LoadProductsAsync(db, items.Select(i => i.ProductId), ct);
        }
        return await QuoteAsync(postalCode, BuildPackages(items, products), ct);
    }

    // Toda cotação passa por aqui. A falha do provedor de frete vira OrderRuleException, que as telas já mostram.
    // Chamado sempre SEM transação aberta: a cotação real é uma chamada HTTP de alguns segundos, e uma transação
    // aberta esse tempo todo prende uma conexão do banco (e, nas edições, a linha do pedido).
    private async Task<IReadOnlyList<ShippingOption>> QuoteAsync(string postalCode, List<ShippingPackage> packages, CancellationToken ct)
    {
        try
        {
            return await shipping.QuoteAsync(postalCode, packages, ct);
        }
        catch (ShippingQuoteException ex)
        {
            // Sem o CEP no log: é dado pessoal digitado pela cliente (regra de LGPD do projeto)
            logger.LogWarning(ex, "Falha ao cotar o frete.");
            throw new OrderRuleException("Could not calculate shipping right now. Try again in a few minutes.");
        }
    }

    // Cria o pedido a partir do carrinho. Preços vêm do banco e o frete é cotado de novo aqui:
    // nada que o navegador mandou é usado como valor.
    // createdBy preenchido = pedido lançado pelo painel (sem limite de pendentes e com prazo maior).
    // shownShippingAmount = o frete que a cliente viu na tela. Se a nova cotação vier MAIOR, o pedido não é criado
    // (ShippingPriceChangedException) e ela confirma de novo vendo o valor certo. Se vier menor, segue: ela paga menos.
    public async Task<Order> CreateAsync(string customerId, IReadOnlyList<CartItem> items, ShippingAddress address,
        string shippingCode, string? createdBy = null, decimal? shownShippingAmount = null, CancellationToken ct = default)
    {
        if (items.Count == 0) throw new OrderRuleException("The cart is empty.");
        var postalCode = PostalCode.Normalize(address.PostalCode) ?? throw new OrderRuleException("Invalid postal code.");
        address.PostalCode = postalCode;
        var options = store.Value;
        var now = clock.GetUtcNow().UtcDateTime;

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        if (createdBy is null)
        {
            var pendingCount = await db.Orders.CountAsync(p => p.CustomerId == customerId && p.Status == OrderStatus.AwaitingPayment, ct);
            if (pendingCount >= options.MaxPendingOrdersPerCustomer)
                throw new OrderRuleException("You already have orders awaiting payment. Pay them or wait for their cancellation before placing another.");
        }

        var products = await LoadProductsAsync(db, items.Select(i => i.ProductId), ct);
        var orderItems = items.Select(i =>
        {
            if (!products.TryGetValue(i.ProductId, out var p) || !p.IsActive)
                throw new OrderRuleException("One of the products in the cart is no longer available.");
            return new OrderItem(p.Id, p.Name, p.Price, i.Quantity);
        }).ToList();

        var quote = await QuoteAsync(postalCode, BuildPackages(items, products), ct);
        var option = quote.FirstOrDefault(o => o.Code == shippingCode)
            ?? throw new OrderRuleException("Invalid shipping option. Calculate shipping again.");
        if (shownShippingAmount is decimal shown && option.Amount > shown)
            throw new ShippingPriceChangedException(option.Amount.ToBrl());

        var deadline = createdBy is null ? TimeSpan.FromMinutes(options.PaymentDeadlineMinutes) : TimeSpan.FromHours(options.AdminPaymentDeadlineHours);
        var order = new Order(customerId, orderItems, address, option, now + deadline, options.PixDiscountPercent, createdBy);

        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            foreach (var item in orderItems)
                await ReduceStockAsync(db, item.ProductId, item.Quantity, item.ProductName, ct);
            db.Orders.Add(order);
            await db.SaveChangesAsync(ct);
            await audit.RecordAsync(db, AuditActions.OrderCreated, AuditEntities.Order, order.Id, $"{order.Code} · {order.Total.ToBrl()}");
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        logger.LogInformation("Pedido {Codigo} criado para o cliente {ClienteId}.", order.Code, customerId);
        return order;
    }

    public async Task<Payment> StartPaymentAsync(int orderId, PaymentMethod method, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var order = await LoadOrderAsync(db, orderId, ct);
        if (order.IsPaymentOverdue(clock.GetUtcNow().UtcDateTime))
            throw new OrderRuleException("The payment deadline for this order has ended.");
        var customer = await db.Users.AsNoTracking().FirstAsync(u => u.Id == order.CustomerId, ct);

        // Antes de abrir uma cobrança nova, fecha no provedor as que estão abertas. Se não conseguir, não abre
        // outra: com duas cobranças abertas, a cliente poderia pagar duas vezes (ex.: o PIX numa aba e o cartão na outra).
        foreach (var open in OpenCharges(order))
            await gateway.CancelChargeAsync(open.Reference, ct);

        var payment = order.StartPayment(method, gateway.Name);
        await SaveAsync(db, ct);

        PaymentStart start;
        try
        {
            start = await gateway.StartAsync(new ChargeRequest(
                order.Id, payment.Id, order.Code, payment.Amount, method, customer.Email ?? "", customer.FullName,
                $"Pedido {order.Code} - Ateliê Marylu", order.PayBy, store.Value.InterestFreeInstallments), ct);
        }
        catch (PaymentGatewayException)
        {
            // A cobrança não abriu (o provedor recusou, caiu ou demorou demais). Sem isso, a tentativa ficaria
            // "Pendente" para sempre, sem link: a tela diria "pagamento iniciado" e esconderia o botão dessa forma.
            // Marcada como falha, a tela mostra "o pagamento não abriu" com "Tentar de novo".
            // Se o provedor chegou a criar o checkout e só a resposta se perdeu, ninguém recebeu o link, então ele
            // não pode ser pago, e vence sozinho no prazo do pedido. Nada muda nas chamadas ao provedor.
            // CancellationToken.None: o registro da falha precisa ser gravado mesmo se quem chamou desistir.
            payment.MarkStartFailed();
            try
            {
                await SaveAsync(db, CancellationToken.None);
            }
            catch (Exception saveError) when (saveError is DbUpdateException or NpgsqlException or OrderRuleException)
            {
                // Não deixa uma falha do banco esconder a do provedor (o motivo real). A tentativa fica pendente
                // sem link, e a tela a mostra como "não abriu" depois de Payment.OpeningWindow.
                logger.LogError(saveError, "Não foi possível gravar a falha do pagamento {PagamentoId} do pedido {PedidoId}.", payment.Id, order.Id);
            }
            logger.LogWarning("O pagamento {PagamentoId} do pedido {PedidoId} não abriu no provedor.", payment.Id, order.Id);
            events.Notify(order.Id);
            throw;
        }
        payment.RecordStart(start.ExternalId, start.PaymentUrl, start.PixCopyPaste, start.ProviderReference);
        await SaveAsync(db, ct);
        events.Notify(order.Id);
        return payment;
    }

    // Existe um pagamento da loja com esta referência neste provedor? Consulta o índice único (Provider, ExternalId).
    public async Task<bool> HasPaymentAsync(string provider, string externalId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Payments.AsNoTracking().AnyAsync(p => p.Provider == provider && p.ExternalId == externalId, ct);
    }

    // Recebe o aviso do provedor (webhook). Retorna false se o pagamento não existir.
    // - Idempotente: o mesmo EventId só é aplicado uma vez.
    // - Se um funcionário alterar o pedido no mesmo instante, tenta de novo com os dados atualizados.
    public async Task<bool> ProcessNotificationAsync(PaymentNotification notification, CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await ProcessNotificationOnceAsync(notification, ct);
            }
            catch (DbUpdateConcurrencyException) when (attempt < 3)
            {
                logger.LogInformation("Conflito ao processar notificação {IdEvento}; tentando de novo.", notification.EventId);
            }
        }
    }

    private async Task<bool> ProcessNotificationOnceAsync(PaymentNotification notification, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var orderId = await db.Payments
            .Where(p => p.Provider == gateway.Name && p.ExternalId == notification.ExternalId)
            .Select(p => (int?)p.OrderId)
            .FirstOrDefaultAsync(ct);
        if (orderId is null) return false;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        db.ProcessedNotifications.Add(new ProcessedNotification { Provider = gateway.Name, EventId = notification.EventId });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            logger.LogInformation("Notificação {IdEvento} já processada; ignorando.", notification.EventId);
            return true;
        }

        var order = await LoadOrderAsync(db, orderId.Value, ct);
        var openBefore = OpenCharges(order);
        var payment = order.Payments.First(p => p.ExternalId == notification.ExternalId);
        string? alert = null;
        var alertAction = AuditActions.PaymentMismatch;
        string? details = null;

        if (notification.Status == PaymentStatus.Approved)
        {
            try
            {
                order.ConfirmPayment(payment, notification.AmountPaid);
            }
            catch (OrderRuleException ex)
            {
                // Ex.: pedido cancelado/expirado e o cliente pagou mesmo assim, ou valor diferente.
                // O dinheiro entrou: fica registrado na auditoria como "pagamento.divergente",
                // que aparece em destaque no resumo do painel para a loja estornar ou resolver.
                payment.UpdateStatus(PaymentStatus.Approved);
                // O alerta fica gravado na auditoria: vai inteiro no idioma da loja, com o motivo traduzido
                alert = $"Pagamento {notification.ExternalId} aprovado no pedido {order.Code}, mas: {localizer.TranslateToStoreLanguage(ex)} Verifique e, se for o caso, estorne.";
            }
        }
        else if (notification.Status == PaymentStatus.Refunded)
        {
            // Estorno (feito pela loja no painel do provedor) ou contestação da cliente no cartão (chargeback)
            alertAction = AuditActions.PaymentReversed;
            switch (order.RegisterReversal(payment))
            {
                case PaymentReversal.OrderCancelled:
                    foreach (var item in order.Items)
                        await RestoreStockAsync(db, item.ProductId, item.Quantity, ct);
                    alert = $"Pagamento {notification.ExternalId} do pedido {order.Code} foi estornado ou contestado. O pedido foi cancelado e o estoque voltou. Não envie.";
                    break;
                case PaymentReversal.AlreadyShipped:
                    alert = $"Pagamento {notification.ExternalId} do pedido {order.Code} foi estornado ou contestado, mas o pedido já foi enviado. Confira no painel do provedor e fale com a cliente.";
                    break;
                case PaymentReversal.StillPaid:
                    details = $"Refunded via {gateway.Name} ({notification.ExternalId}); o pedido continua pago por outro pagamento";
                    break;
            }
        }
        else if (payment.Status != PaymentStatus.Approved)
        {
            payment.UpdateStatus(notification.Status);
        }
        else if (notification.Status is PaymentStatus.Cancelled or PaymentStatus.Rejected)
        {
            // Transição inesperada: o provedor diz que um pagamento já aprovado foi cancelado ou recusado.
            // Pode ser um estorno num formato que a loja não conhece. Não muda nada sozinho (o pedido pode até já
            // ter sido enviado), mas também não fica em silêncio: vira alerta para alguém conferir no provedor.
            alertAction = AuditActions.PaymentReversed;
            alert = $"O provedor informou {notification.Status} para o pagamento {notification.ExternalId} do pedido {order.Code}, que já estava aprovado. Confira no painel do provedor se houve estorno antes de enviar.";
        }

        await audit.RecordAsync(db, alert is null ? AuditActions.PaymentNotified : alertAction, AuditEntities.Order, order.Id,
            alert ?? details ?? $"{notification.Status} via {gateway.Name} ({notification.ExternalId})", Author.Automated);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        events.Notify(order.Id);
        if (alert is not null) logger.LogError("ALERTA: {Alerta}", alert);
        ScheduleClosingOfFinishedCharges(order, openBefore);
        return true;
    }

    // Para quando o cliente pagou por fora (PIX direto, dinheiro na retirada...)
    public Task RecordManualPaymentAsync(int orderId, PaymentMethod method, CancellationToken ct = default) =>
        ChangeAsync(orderId, AuditActions.PaymentManual, (order, _) =>
        {
            var payment = order.StartPayment(method, ManualProvider);
            payment.RecordStart($"manual-{Guid.NewGuid():N}", null, null);
            order.ConfirmPayment(payment);
            return Task.FromResult<string?>($"{method} · {payment.Amount.ToBrl()}");
        }, ct: ct);

    public Task CancelAsync(int orderId, string? reason = null, CancellationToken ct = default) =>
        ChangeAsync(orderId, AuditActions.OrderCancelled, async (order, db) =>
        {
            order.Cancel(reason);
            foreach (var item in order.Items)
                await RestoreStockAsync(db, item.ProductId, item.Quantity, ct);
            return reason;
        }, ct: ct);

    // Se o provedor não responder na conciliação, o pedido espera até este tempo além do prazo. Depois expira
    // assim mesmo, para um provedor fora do ar não segurar o estoque para sempre (um pagamento tardio vira alerta).
    public static readonly TimeSpan ReconciliationGrace = TimeSpan.FromHours(1);

    // Tarefa periódica: cancela pedidos cujo prazo de pagamento acabou e devolve o estoque
    public async Task<int> ExpireOverdueAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        List<int> overdue;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            overdue = await db.Orders
                .Where(p => p.Status == OrderStatus.AwaitingPayment && p.PayBy < now)
                .Select(p => p.Id).ToListAsync(ct);
        }

        var cancelledCount = 0;
        foreach (var id in overdue)
        {
            if (!await ReadyToExpireAfterReconcilingAsync(id, now, ct)) continue;
            try
            {
                var expired = await ChangeAsync(id, AuditActions.OrderExpired, async (order, db) =>
                {
                    // Confere de novo: pode ter sido pago entre a consulta e agora
                    if (!order.IsPaymentOverdue(now)) return null;
                    order.Cancel("Prazo de pagamento expirado");
                    foreach (var item in order.Items)
                        await RestoreStockAsync(db, item.ProductId, item.Quantity, ct);
                    return "Prazo de pagamento expirado";
                }, Author.Automated, ct);
                if (expired) cancelledCount++;
            }
            catch (Exception ex) when (ex is OrderRuleException or DbUpdateException)
            {
                logger.LogWarning(ex, "Não foi possível expirar o pedido {PedidoId}", id);
            }
        }
        return cancelledCount;
    }

    // Conciliação antes de expirar: pergunta ao provedor se alguma cobrança pendente do pedido foi paga.
    // Se foi, confirma pelo mesmo caminho do webhook (e o EventId evita processar duas vezes).
    // Devolve false quando o pedido não deve expirar agora: foi pago, ou o provedor não respondeu e ainda há folga.
    private async Task<bool> ReadyToExpireAfterReconcilingAsync(int orderId, DateTime now, CancellationToken ct)
    {
        List<(string ExternalId, string? ProviderReference)> pending;
        DateTime payBy;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var order = await db.Orders.AsNoTracking().Include(o => o.Payments).FirstOrDefaultAsync(o => o.Id == orderId, ct);
            if (order is null) return false;
            payBy = order.PayBy;
            pending = order.Payments
                .Where(p => p.Status == PaymentStatus.Pending && p.Provider == gateway.Name && p.ExternalId is not null)
                .Select(p => (p.ExternalId!, p.ProviderReference))
                .ToList();
        }

        foreach (var (externalId, providerReference) in pending)
        {
            PaymentNotification? status;
            try
            {
                status = await gateway.QueryStatusAsync(externalId, providerReference, ct);
            }
            catch (PaymentGatewayException ex)
            {
                if (now < payBy + ReconciliationGrace)
                {
                    logger.LogWarning(ex, "Não foi possível consultar o provedor antes de expirar o pedido {PedidoId}; tenta de novo na próxima rodada.", orderId);
                    return false;
                }
                logger.LogError(ex, "ALERTA: o provedor não respondeu e o pedido {PedidoId} expira mesmo assim. Um pagamento tardio aparecerá como divergente.", orderId);
                return true;
            }

            if (status?.Status == PaymentStatus.Approved)
            {
                logger.LogWarning("Pedido {PedidoId} estava pago no provedor sem o aviso ter chegado; confirmado pela conciliação.", orderId);
                await ProcessNotificationAsync(status, ct);
                return false;
            }
        }
        return true;
    }

    // Nas três edições abaixo, o frete é cotado ANTES da transação (PrepareRequoteAsync), com os itens e o CEP
    // como ficarão depois da mudança. Dentro da transação, ApplyRequote só confere que nada mudou no meio e aplica.
    public async Task ChangeQuantityAsync(int orderId, int productId, int quantity, CancellationToken ct = default)
    {
        var requote = await PrepareRequoteAsync(orderId, items => items
            .Select(i => i.ProductId == productId ? i with { Quantity = quantity } : i)
            .Where(i => i.Quantity > 0), newPostalCode: null, ct);

        await ChangeAsync(orderId, AuditActions.OrderItemsChanged, async (order, db) =>
        {
            var name = order.Items.FirstOrDefault(i => i.ProductId == productId)?.ProductName ?? "";
            var difference = order.ChangeQuantity(productId, quantity);
            if (difference > 0) await ReduceStockAsync(db, productId, difference, name, ct);
            else if (difference < 0) await RestoreStockAsync(db, productId, -difference, ct);
            ApplyRequote(order, requote);
            return $"{name}: quantidade {quantity}";
        }, ct: ct);
    }

    public async Task AddItemAsync(int orderId, int productId, int quantity, CancellationToken ct = default)
    {
        // Mesma regra do Order.AddItem: produto que já está no pedido só soma a quantidade
        var requote = await PrepareRequoteAsync(orderId, items => items.Any(i => i.ProductId == productId)
            ? items.Select(i => i.ProductId == productId ? i with { Quantity = i.Quantity + quantity } : i)
            : items.Append(new CartItem(productId, quantity)), newPostalCode: null, ct);

        await ChangeAsync(orderId, AuditActions.OrderItemsChanged, async (order, db) =>
        {
            var product = await db.Products.FirstOrDefaultAsync(p => p.Id == productId && p.IsActive, ct)
                ?? throw new OrderRuleException("Product not found.");
            order.AddItem(new OrderItem(product.Id, product.Name, product.Price, quantity));
            await ReduceStockAsync(db, product.Id, quantity, product.Name, ct);
            ApplyRequote(order, requote);
            return $"+{quantity} {product.Name}";
        }, ct: ct);
    }

    public async Task ChangeDeliveryAsync(int orderId, ShippingAddress address, string? notes, CancellationToken ct = default)
    {
        address.PostalCode = PostalCode.Normalize(address.PostalCode) ?? throw new OrderRuleException("Invalid postal code.");
        var requote = await PrepareRequoteAsync(orderId, items => items, address.PostalCode, ct);

        await ChangeAsync(orderId, AuditActions.OrderDeliveryChanged, (order, _) =>
        {
            order.ChangeAddress(address);
            order.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
            ApplyRequote(order, requote);
            return Task.FromResult<string?>($"CEP {address.PostalCode}");
        }, ct: ct);
    }

    public Task StartPreparationAsync(int orderId, CancellationToken ct = default) =>
        ChangeAsync(orderId, AuditActions.OrderPreparing, (p, _) => { p.StartPreparation(); return Task.FromResult<string?>(null); }, ct: ct);

    public async Task MarkShippedAsync(int orderId, string? trackingCode, CancellationToken ct = default)
    {
        // Enviar não tem volta: confere antes com o provedor se o pagamento continua valendo
        await EnsurePaymentStillValidAsync(orderId, ct);
        await ChangeAsync(orderId, AuditActions.OrderShipped, (p, _) => { p.MarkShipped(trackingCode); return Task.FromResult(p.TrackingCode); }, ct: ct);
    }

    // Conferência no momento irreversível. Um aviso de estorno ou de contestação (chargeback) pode ter se perdido
    // (ex.: a loja fora do ar num deploy), e o pedido continuaria "Pago". Pagamento manual (ex.: Minizinha) não tem
    // o que consultar. Se o provedor não responder, o envio espera: melhor atrasar que mandar sem o dinheiro.
    private async Task EnsurePaymentStillValidAsync(int orderId, CancellationToken ct)
    {
        List<(string ExternalId, string? ProviderReference)> approved;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            approved = await db.Payments.AsNoTracking()
                .Where(p => p.OrderId == orderId && p.Status == PaymentStatus.Approved && p.Provider == gateway.Name && p.ExternalId != null)
                .Select(p => new ValueTuple<string, string?>(p.ExternalId!, p.ProviderReference))
                .ToListAsync(ct);
        }

        foreach (var (externalId, providerReference) in approved)
        {
            PaymentNotification? status;
            try
            {
                status = await gateway.QueryStatusAsync(externalId, providerReference, ct);
            }
            catch (PaymentGatewayException ex)
            {
                logger.LogWarning(ex, "Não foi possível conferir o pagamento do pedido {PedidoId} antes do envio.", orderId);
                throw new OrderRuleException("Could not confirm the payment with the provider. Try again in a few minutes.");
            }

            if (status?.Status is PaymentStatus.Refunded or PaymentStatus.Cancelled or PaymentStatus.Rejected)
            {
                // Aplica pelo mesmo caminho do webhook: o pedido é cancelado (ou vira alerta), e o estoque volta
                await ProcessNotificationAsync(status, ct);
                throw new OrderRuleException("The payment for this order was refunded or disputed at the provider. Do not ship it.");
            }
        }
    }

    public Task MarkDeliveredAsync(int orderId, CancellationToken ct = default) =>
        ChangeAsync(orderId, AuditActions.OrderDelivered, (p, _) => { p.MarkDelivered(); return Task.FromResult<string?>(null); }, ct: ct);

    // Carrega, altera, registra auditoria e grava tudo numa transação.
    // A alteração devolve o texto de detalhe da auditoria (ou null para "nada a fazer").
    // Devolve false quando não havia nada a fazer.
    private async Task<bool> ChangeAsync(int orderId, string action, Func<Order, ApplicationDbContext, Task<string?>> change,
        Author? author = null, CancellationToken ct = default)
    {
        Order order;
        List<OpenCharge> openBefore;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            order = await LoadOrderAsync(db, orderId, ct);
            openBefore = OpenCharges(order);
            var statusBefore = order.Status;
            var details = await change(order, db);
            if (details is null && order.Status == statusBefore && !db.ChangeTracker.HasChanges())
                return false;
            await audit.RecordAsync(db, action, AuditEntities.Order, order.Id, details, author);
            await SaveAsync(db, ct);
            await transaction.CommitAsync(ct);
        }

        events.Notify(orderId);
        // Cancelar, editar itens ou frete, ou registrar pagamento manual cancela as cobranças pendentes
        ScheduleClosingOfFinishedCharges(order, openBefore);
        return true;
    }

    private sealed record OpenCharge(int PaymentId, string Reference);

    // Cobranças abertas no provedor atual (ex.: checkout do PagBank), que ainda aceitam pagamento
    private List<OpenCharge> OpenCharges(Order order) =>
        order.Payments
            .Where(p => p.Status == PaymentStatus.Pending && p.Provider == gateway.Name && p.ProviderReference is not null)
            .Select(p => new OpenCharge(p.Id, p.ProviderReference!))
            .ToList();

    // Depois de gravar: manda fechar no provedor as cobranças que deixaram de estar pendentes aqui, canceladas OU pagas.
    // A paga também: o sandbox mostrou que o checkout do PagBank continua ativo depois do pagamento, e a cliente
    // conseguiria pagar de novo no mesmo link. Vai para a fila em segundo plano (IChargeCloser): a alteração já está
    // gravada e não volta atrás, e quem chamou (o webhook, uma tela do painel) não espera o provedor.
    private void ScheduleClosingOfFinishedCharges(Order order, List<OpenCharge> openBefore)
    {
        // O checkout vence junto com o prazo do pedido: depois dele, não há o que fechar
        if (openBefore.Count == 0 || clock.GetUtcNow().UtcDateTime > order.PayBy) return;

        var finished = openBefore.Where(open =>
            order.Payments.FirstOrDefault(p => p.Id == open.PaymentId)?.Status is not (null or PaymentStatus.Pending));
        foreach (var open in finished)
            chargeCloser.Enqueue(new ChargeToClose(open.Reference, order.Id, order.PayBy));
    }

    private static async Task SaveAsync(ApplicationDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new OrderRuleException(ConcurrencyMessage);
        }
    }

    // Cotação feita antes da transação, para os itens e o CEP que o pedido terá depois da edição
    private sealed record Requote(string PostalCode, List<CartItem> Items, IReadOnlyList<ShippingOption> Options);

    // Devolve null quando não há o que recotar: o frete só muda antes do pagamento, e no endereço só se o CEP mudar.
    // Se o pedido já foi pago, a própria entidade recusa a troca de itens dentro do ChangeAsync.
    private async Task<Requote?> PrepareRequoteAsync(int orderId, Func<IEnumerable<CartItem>, IEnumerable<CartItem>> change,
        string? newPostalCode, CancellationToken ct)
    {
        List<CartItem> items;
        string postalCode;
        Dictionary<int, Product> products;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var order = await db.Orders.AsNoTracking().Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == orderId, ct);
            if (order is null || !order.CanChangeItems) return null;
            if (newPostalCode is not null && newPostalCode == order.Address.PostalCode) return null;

            items = Snapshot(change(order.Items.Select(i => new CartItem(i.ProductId, i.Quantity))));
            postalCode = newPostalCode ?? order.Address.PostalCode;
            products = await LoadProductsAsync(db, items.Select(i => i.ProductId), ct);
        }

        return new Requote(postalCode, items, await QuoteAsync(postalCode, BuildPackages(items, products), ct));
    }

    // Dentro da transação: se o pedido mudou entre a cotação e agora (outra pessoa editando ao mesmo tempo),
    // a cotação não vale para ele. Melhor pedir para tentar de novo do que gravar um frete calculado para outros itens.
    private void ApplyRequote(Order order, Requote? requote)
    {
        if (requote is null) return;
        var current = Snapshot(order.Items.Select(i => new CartItem(i.ProductId, i.Quantity)));
        if (order.Address.PostalCode != requote.PostalCode || !current.SequenceEqual(requote.Items))
            throw new OrderRuleException(ConcurrencyMessage);

        // A modalidade deixou de atender o pedido (CEP novo fora da área, ou pacote acima do limite dela).
        // Manter o valor antigo faria a loja cobrar menos do que a etiqueta custa, ou vender um envio que não existe.
        // A edição é recusada (a transação desfaz o estoque já mexido). O painel não troca a modalidade: o caminho é
        // cancelar e criar outro pedido com outra forma de entrega (decisão de 21/11/2026, AML-0039).
        var same = requote.Options.FirstOrDefault(o => o.Code == order.ShippingCode)
            ?? throw new OrderRuleException(
                "The shipping method \"{0}\" no longer serves this order (new address or package size). Cancel this order and create a new one with another shipping method.",
                order.ShippingName);
        order.SetShipping(same);
    }

    // Ordem fixa e sem itens zerados, para comparar duas listas de itens
    private static List<CartItem> Snapshot(IEnumerable<CartItem> items) =>
        items.Where(i => i.Quantity > 0).OrderBy(i => i.ProductId).ToList();

    private static async Task<Order> LoadOrderAsync(ApplicationDbContext db, int orderId, CancellationToken ct) =>
        await db.Orders.Include(p => p.Items).Include(p => p.Payments).FirstOrDefaultAsync(p => p.Id == orderId, ct)
            ?? throw new OrderRuleException("Order not found.");

    private static async Task<Dictionary<int, Product>> LoadProductsAsync(ApplicationDbContext db, IEnumerable<int> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await db.Products.AsNoTracking().Where(p => list.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
    }

    private static List<ShippingPackage> BuildPackages(IEnumerable<CartItem> items, Dictionary<int, Product> products) =>
        items.Where(i => products.ContainsKey(i.ProductId))
            .Select(i =>
            {
                var p = products[i.ProductId];
                return new ShippingPackage(p.WeightGrams, p.HeightCm, p.WidthCm, p.LengthCm, i.Quantity, p.Price);
            }).ToList();

    // UPDATE com condição no próprio banco: se duas pessoas comprarem a última peça ao mesmo tempo,
    // só uma consegue (a outra afeta 0 linhas e recebe o erro).
    private static async Task ReduceStockAsync(ApplicationDbContext db, int productId, int quantity, string name, CancellationToken ct)
    {
        var rows = await db.Products
            .Where(p => p.Id == productId && p.Stock >= quantity)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, p => p.Stock - quantity), ct);
        if (rows == 0)
            throw new OrderRuleException("Not enough stock for \"{0}\".", name);
    }

    private static Task RestoreStockAsync(ApplicationDbContext db, int productId, int quantity, CancellationToken ct) =>
        db.Products.Where(p => p.Id == productId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, p => p.Stock + quantity), ct);
}
