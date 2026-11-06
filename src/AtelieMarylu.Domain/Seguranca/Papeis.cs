namespace AtelieMarylu.Domain.Seguranca;

// Papéis fixos do sistema. Os demais papéis (roles) do Identity são "grupos de funcionários"
// criados pelo Admin, cada um com o seu conjunto de permissões.
public static class Papeis
{
    public const string Admin = "Admin";
    public const string Cliente = "Cliente";

    public static bool EhPapelDoSistema(string nome) => nome is Admin or Cliente;
}
