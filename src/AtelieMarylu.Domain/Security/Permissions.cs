namespace AtelieMarylu.Domain.Security;

// Cada permissão vira um claim do tipo "permission", concedido direto ao usuário
// (individual) ou ao grupo (role claim). O Admin tem todas automaticamente.
public static class Permissions
{
    public const string ClaimType = "permission";

    public const string ProductsManage = "products.manage";
    public const string OrdersView = "orders.view";
    public const string OrdersCreate = "orders.create";
    public const string OrdersEdit = "orders.edit";
    public const string OrdersCancel = "orders.cancel";
    public const string CustomersView = "customers.view";

    public static readonly IReadOnlyDictionary<string, string> All = new Dictionary<string, string>
    {
        [ProductsManage] = "Create and edit products",
        [OrdersView] = "View orders",
        [OrdersCreate] = "Create orders from the admin",
        [OrdersEdit] = "Edit orders and change status",
        [OrdersCancel] = "Cancel orders",
        [CustomersView] = "View registered customers",
    };

    // Política que libera o painel para quem tem qualquer permissão
    public const string AccessAdmin = "admin.access";
}
