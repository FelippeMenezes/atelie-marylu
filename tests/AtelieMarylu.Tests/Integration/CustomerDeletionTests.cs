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
