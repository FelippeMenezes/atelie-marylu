using System.Security.Claims;
using AtelieMarylu.Domain.Auditing;
using AtelieMarylu.Domain.Orders;
using AtelieMarylu.Domain.Security;
using AtelieMarylu.Domain.Shipping;
using AtelieMarylu.Infrastructure.Data;
using AtelieMarylu.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AtelieMarylu.Tests.Integration;

// Exclusão de cliente pelo painel (AccessService.DeleteCustomerAsync): o e-mail fica livre para um
// cadastro novo, os pedidos ficam, e a auditoria registra a exclusão sem guardar o e-mail.
public class CustomerDeletionTests(TestDatabase database) : IClassFixture<TestDatabase>
{
    private const string AdminId = "admin-que-exclui";
    private const string Reason = "Cadastro falso";

    [FactWithDatabase]
    public async Task Customer_without_orders_is_deleted_and_email_is_free()
    {
        using var scope = database.Services.CreateScope();
        var users = Users(scope);
        var customer = await CreateUserAsync(users);
        var email = customer.Email!;

        await Service(scope).DeleteCustomerAsync(customer.Id, AdminId, Reason, email.ToUpperInvariant());

        Assert.Null(await users.FindByIdAsync(customer.Id));
        // O e-mail pode ser usado num cadastro novo, normal
        var again = new ApplicationUser { UserName = email, Email = email, FullName = "Cadastro novo" };
        Assert.True((await users.CreateAsync(again, "Nova!Senha123")).Succeeded);

        var entry = await AuditEntryAsync(customer.Id);
        Assert.Contains("equipe de gestão técnica", entry.Details);
        Assert.Contains(Reason, entry.Details);
        Assert.Contains("Conta apagada", entry.Details);
        Assert.DoesNotContain(email, entry.Details ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [FactWithDatabase]
    public async Task Customer_with_orders_is_anonymized_orders_are_kept_and_email_is_free()
    {
        using var scope = database.Services.CreateScope();
        var users = Users(scope);
        var customer = await CreateUserAsync(users);
        var email = customer.Email!;
        var orderId = await CreateOrderAsync(customer.Id);

        await Service(scope).DeleteCustomerAsync(customer.Id, AdminId, Reason, email);

        var anonymized = (await users.FindByIdAsync(customer.Id))!;
        Assert.NotEqual(email, anonymized.Email);
        Assert.Equal("Cliente removido", anonymized.FullName);
        await using (var db = database.CreateDbContext())
            Assert.Equal(customer.Id, (await db.Orders.SingleAsync(o => o.Id == orderId)).CustomerId);
        Assert.Null(await users.FindByEmailAsync(email));

        var entry = await AuditEntryAsync(customer.Id);
        Assert.Contains("Conta anonimizada; pedidos mantidos.", entry.Details);
    }

    [FactWithDatabase]
    public async Task Deletion_scrubs_the_customer_emails_but_keeps_the_evidence_and_the_store_notices()
    {
        // O dado pessoal sai já (sem esperar os 30 dias); tipo, pedido e datas ficam como prova do envio (Decreto
        // 7.962, LGPD art. 16, I). A pendente vira "pulada": não se manda e-mail para uma conta excluída.
        using var scope = database.Services.CreateScope();
        var users = Users(scope);
        var customer = await CreateUserAsync(users);
        var email = customer.Email!;
        var orderId = await CreateOrderAsync(customer.Id);
        OutboxEmail sent, pending, store;
        await using (var db = database.CreateDbContext())
        {
            sent = Outbox(OutboxEmailKinds.OrderReceived, email, orderId, sent: true);
            pending = Outbox(OutboxEmailKinds.PaymentConfirmed, email, orderId, sent: false);
            store = Outbox(OutboxEmailKinds.CancellationStaffNotice, "contato@loja.teste", orderId, sent: false);
            db.OutboxEmails.AddRange(sent, pending, store);
            await db.SaveChangesAsync();
        }

        await Service(scope).DeleteCustomerAsync(customer.Id, AdminId, Reason, email);

        await using (var db = database.CreateDbContext())
        {
            var rows = await db.OutboxEmails.AsNoTracking().Where(e => e.OrderId == orderId).ToDictionaryAsync(e => e.Id);
            Assert.Equal(3, rows.Count);

            var keptSent = rows[sent.Id];
            Assert.Null(keptSent.Recipient);
            Assert.Equal("{}", keptSent.Payload);
            Assert.NotNull(keptSent.SentAt);
            Assert.Equal(OutboxEmailKinds.OrderReceived, keptSent.Kind);

            var skipped = rows[pending.Id];
            Assert.Null(skipped.Recipient);
            Assert.Equal("{}", skipped.Payload);
            Assert.NotNull(skipped.SkippedAt);
            Assert.Equal("Skipped: account deleted", skipped.LastError);

            Assert.Equal("contato@loja.teste", rows[store.Id].Recipient);
            Assert.Null(rows[store.Id].SkippedAt);
        }
    }

    [FactWithDatabase]
    public async Task Deletion_also_scrubs_emails_sent_to_an_old_address()
    {
        // A cliente fez o pedido com um e-mail e depois trocou. As linhas com o endereço antigo (nome, e-mail e
        // endereço de entrega no payload) também saem com a conta.
        using var scope = database.Services.CreateScope();
        var users = Users(scope);
        var customer = await CreateUserAsync(users);
        var orderId = await CreateOrderAsync(customer.Id);
        await using (var db = database.CreateDbContext())
        {
            db.OutboxEmails.AddRange(
                Outbox(OutboxEmailKinds.OrderReceived, "antigo@teste.local", orderId, sent: true),
                Outbox(OutboxEmailKinds.CancellationReceived, "antigo@teste.local", orderId, sent: false),
                Outbox(OutboxEmailKinds.CancellationStaffNotice, "contato@loja.teste", orderId, sent: true));
            await db.SaveChangesAsync();
        }

        await Service(scope).DeleteCustomerAsync(customer.Id, AdminId, Reason, customer.Email!);

        await using (var db = database.CreateDbContext())
        {
            Assert.False(await db.OutboxEmails.AnyAsync(e => e.Recipient == "antigo@teste.local"));
            var rows = await db.OutboxEmails.Where(e => e.OrderId == orderId).ToListAsync();
            Assert.Equal(3, rows.Count);
            Assert.All(rows.Where(e => e.Kind != OutboxEmailKinds.CancellationStaffNotice), e => Assert.Equal("{}", e.Payload));
            Assert.Equal("contato@loja.teste", Assert.Single(rows, e => e.Recipient is not null).Recipient);
        }
    }

    [FactWithDatabase]
    public async Task Deletion_keeps_emails_of_other_customers()
    {
        using var scope = database.Services.CreateScope();
        var users = Users(scope);
        var customer = await CreateUserAsync(users);
        var other = await CreateUserAsync(users);
        var otherOrderId = await CreateOrderAsync(other.Id);
        await using (var db = database.CreateDbContext())
        {
            db.OutboxEmails.Add(Outbox(OutboxEmailKinds.OrderReceived, other.Email!, otherOrderId, sent: true));
            await db.SaveChangesAsync();
        }

        await Service(scope).DeleteCustomerAsync(customer.Id, AdminId, Reason, customer.Email!);

        await using (var db = database.CreateDbContext())
            Assert.Equal(other.Email, (await db.OutboxEmails.SingleAsync(e => e.OrderId == otherOrderId)).Recipient);
    }

    private static OutboxEmail Outbox(string kind, string to, int orderId, bool sent) => new()
    {
        Kind = kind, Recipient = to, Language = "pt-BR", OrderId = orderId, Payload = "{}",
        CreatedAt = DateTime.UtcNow, NextAttemptAt = DateTime.UtcNow, SentAt = sent ? DateTime.UtcNow : null,
    };

    [FactWithDatabase]
    public async Task Refuses_admin_self_wrong_email_missing_reason_and_staff()
    {
        using var scope = database.Services.CreateScope();
        var users = Users(scope);
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roles.RoleExistsAsync(AppRoles.Admin)) await roles.CreateAsync(new IdentityRole(AppRoles.Admin));

        var admin = await CreateUserAsync(users);
        await users.AddToRoleAsync(admin, AppRoles.Admin);
        var staff = await CreateUserAsync(users);
        await users.AddClaimAsync(staff, new Claim(Permissions.ClaimType, Permissions.OrdersView));
        var customer = await CreateUserAsync(users);
        var service = Service(scope);

        await Refused(service.DeleteCustomerAsync(admin.Id, AdminId, Reason, admin.Email!), "Admin accounts cannot be deleted here.");
        await Refused(service.DeleteCustomerAsync(customer.Id, customer.Id, Reason, customer.Email!), "You cannot delete your own account here.");
        await Refused(service.DeleteCustomerAsync(customer.Id, AdminId, Reason, "outro@teste.local"), "The email typed does not match this customer's email.");
        await Refused(service.DeleteCustomerAsync(customer.Id, AdminId, "  ", customer.Email!), "Enter the reason for the deletion.");
        await Refused(service.DeleteCustomerAsync(staff.Id, AdminId, Reason, staff.Email!), "Remove this person's admin access before deleting the account.");

        // Nada foi apagado
        foreach (var id in new[] { admin.Id, staff.Id, customer.Id })
            Assert.NotNull(await users.FindByIdAsync(id));
    }

    private static async Task Refused(Task action, string message)
    {
        var ex = await Assert.ThrowsAsync<AccessException>(() => action);
        Assert.Equal(message, ex.Message);
    }

    private async Task<AuditEntry> AuditEntryAsync(string userId)
    {
        await using var db = database.CreateDbContext();
        return await db.AuditEntries.SingleAsync(a => a.Action == AuditActions.CustomerDeletedByStaff && a.EntityId == userId);
    }

    private async Task<int> CreateOrderAsync(string customerId)
    {
        var product = await database.CreateProductAsync(stock: 5);
        await using var db = database.CreateDbContext();
        var order = new Order(customerId, [new OrderItem(product.Id, product.Name, product.Price, 1)],
            new ShippingAddress { PostalCode = "20000000", Street = "Rua A", Number = "1", District = "Centro", City = "Rio de Janeiro", State = "RJ" },
            new ShippingOption("pac", "PAC", 10m, 8), DateTime.UtcNow.AddDays(1));
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    }

    private static UserManager<ApplicationUser> Users(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

    private AccessService Service(IServiceScope scope)
    {
        var users = Users(scope);
        var audit = new AuditService(new CurrentUserService(new ServiceCollection().BuildServiceProvider(), new HttpContextAccessor()), database);
        return new AccessService(users, scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>(), database, audit,
            new PersonalDataService(users, database, audit), NullLogger<AccessService>.Instance);
    }

    private static async Task<ApplicationUser> CreateUserAsync(UserManager<ApplicationUser> users)
    {
        var email = $"{Guid.NewGuid():N}@teste.local";
        var user = new ApplicationUser { UserName = email, Email = email, FullName = "Cliente Falso" };
        Assert.True((await users.CreateAsync(user, "Original!123")).Succeeded);
        return user;
    }
}
