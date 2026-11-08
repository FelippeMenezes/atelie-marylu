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
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var products = await LoadProductsAsync(db, items.Select(i => i.ProductId), ct);
        return await shipping.QuoteAsync(postalCode, BuildPackages(items, products), ct);
    }

    // Cria o pedido a partir do carrinho. Preços vêm do banco e o frete é cotado de novo aqui:
    // nada que o navegador mandou é usado como valor.
    // createdBy preenchido = pedido lançado pelo painel (sem limite de pendentes e com prazo maior).
    public async Task<Order> CreateAsync(string customerId, IReadOnlyList<CartItem> items, ShippingAddress address,
        string shippingCode, string? createdBy = null, CancellationToken ct = default)
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

        var quote = await shipping.QuoteAsync(postalCode, BuildPackages(items, products), ct);
        var option = quote.FirstOrDefault(o => o.Code == shippingCode)
            ?? throw new OrderRuleException("Invalid shipping option. Calculate shipping again.");

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

        var payment = order.StartPayment(method, gateway.Name);
        await SaveAsync(db, ct);

        var start = await gateway.StartAsync(new ChargeRequest(
            order.Id, order.Code, payment.Amount, method, customer.Email ?? "", customer.FullName,
            $"Pedido {order.Code} - Ateliê Marylu", order.PayBy), ct);
        payment.RecordStart(start.ExternalId, start.PaymentUrl, start.PixCopyPaste);
        await SaveAsync(db, ct);
        events.Notify(order.Id);
        return payment;
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
        var payment = order.Payments.First(p => p.ExternalId == notification.ExternalId);
        string? alert = null;

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
        else if (payment.Status != PaymentStatus.Approved || notification.Status == PaymentStatus.Refunded)
        {
            payment.UpdateStatus(notification.Status);
        }

        await audit.RecordAsync(db, alert is null ? AuditActions.PaymentNotified : AuditActions.PaymentMismatch, AuditEntities.Order, order.Id,
            alert ?? $"{notification.Status} via {gateway.Name} ({notification.ExternalId})", Author.Automated);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        events.Notify(order.Id);
        if (alert is not null) logger.LogError("ALERTA: {Alerta}", alert);
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

    public Task ChangeQuantityAsync(int orderId, int productId, int quantity, CancellationToken ct = default) =>
        ChangeAsync(orderId, AuditActions.OrderItemsChanged, async (order, db) =>
        {
            var name = order.Items.FirstOrDefault(i => i.ProductId == productId)?.ProductName ?? "";
            var difference = order.ChangeQuantity(productId, quantity);
            if (difference > 0) await ReduceStockAsync(db, productId, difference, name, ct);
            else if (difference < 0) await RestoreStockAsync(db, productId, -difference, ct);
            await RequoteShippingAsync(db, order, ct);
            return $"{name}: quantidade {quantity}";
        }, ct: ct);

    public Task AddItemAsync(int orderId, int productId, int quantity, CancellationToken ct = default) =>
        ChangeAsync(orderId, AuditActions.OrderItemsChanged, async (order, db) =>
        {
            var product = await db.Products.FirstOrDefaultAsync(p => p.Id == productId && p.IsActive, ct)
                ?? throw new OrderRuleException("Product not found.");
            order.AddItem(new OrderItem(product.Id, product.Name, product.Price, quantity));
            await ReduceStockAsync(db, product.Id, quantity, product.Name, ct);
            await RequoteShippingAsync(db, order, ct);
            return $"+{quantity} {product.Name}";
        }, ct: ct);

    public Task ChangeDeliveryAsync(int orderId, ShippingAddress address, string? notes, CancellationToken ct = default) =>
        ChangeAsync(orderId, AuditActions.OrderDeliveryChanged, async (order, db) =>
        {
            address.PostalCode = PostalCode.Normalize(address.PostalCode) ?? throw new OrderRuleException("Invalid postal code.");
            var postalCodeChanged = address.PostalCode != order.Address.PostalCode;
            order.ChangeAddress(address);
            order.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
            if (postalCodeChanged && order.CanChangeItems) await RequoteShippingAsync(db, order, ct);
            return $"CEP {address.PostalCode}";
        }, ct: ct);

    public Task StartPreparationAsync(int orderId, CancellationToken ct = default) =>
        ChangeAsync(orderId, AuditActions.OrderPreparing, (p, _) => { p.StartPreparation(); return Task.FromResult<string?>(null); }, ct: ct);

    public Task MarkShippedAsync(int orderId, string? trackingCode, CancellationToken ct = default) =>
        ChangeAsync(orderId, AuditActions.OrderShipped, (p, _) => { p.MarkShipped(trackingCode); return Task.FromResult(p.TrackingCode); }, ct: ct);

    public Task MarkDeliveredAsync(int orderId, CancellationToken ct = default) =>
        ChangeAsync(orderId, AuditActions.OrderDelivered, (p, _) => { p.MarkDelivered(); return Task.FromResult<string?>(null); }, ct: ct);

    // Carrega, altera, registra auditoria e grava tudo numa transação.
    // A alteração devolve o texto de detalhe da auditoria (ou null para "nada a fazer").
    // Devolve false quando não havia nada a fazer.
    private async Task<bool> ChangeAsync(int orderId, string action, Func<Order, ApplicationDbContext, Task<string?>> change,
        Author? author = null, CancellationToken ct = default)
    {
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var order = await LoadOrderAsync(db, orderId, ct);
            var statusBefore = order.Status;
            var details = await change(order, db);
            if (details is null && order.Status == statusBefore && !db.ChangeTracker.HasChanges())
                return false;
            await audit.RecordAsync(db, action, AuditEntities.Order, order.Id, details, author);
            await SaveAsync(db, ct);
            await transaction.CommitAsync(ct);
        }

        events.Notify(orderId);
        return true;
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

    private async Task RequoteShippingAsync(ApplicationDbContext db, Order order, CancellationToken ct)
    {
        var items = order.Items.Select(i => new CartItem(i.ProductId, i.Quantity)).ToList();
        var products = await LoadProductsAsync(db, items.Select(i => i.ProductId), ct);
        var options = await shipping.QuoteAsync(order.Address.PostalCode, BuildPackages(items, products), ct);
        var same = options.FirstOrDefault(o => o.Code == order.ShippingCode);
        if (same is not null) order.SetShipping(same);
    }

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
                return new ShippingPackage(p.WeightGrams, p.HeightCm, p.WidthCm, p.LengthCm, i.Quantity);
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
