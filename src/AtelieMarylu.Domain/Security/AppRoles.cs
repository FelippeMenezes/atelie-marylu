namespace AtelieMarylu.Domain.Security;

// Papéis fixos do sistema. Os demais papéis (roles) do Identity são "grupos de funcionários"
// criados pelo Admin, cada um com o seu conjunto de permissões.
public static class AppRoles
{
    public const string Admin = "Admin";
    public const string Customer = "Customer";

    public static bool IsSystemRole(string name) => name is Admin or Customer;
}
