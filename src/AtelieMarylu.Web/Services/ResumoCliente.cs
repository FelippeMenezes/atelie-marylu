namespace AtelieMarylu.Web.Services;

// Uma linha da lista de clientes do painel
public record ResumoCliente(string Id, string Nome, string? Email, string? Telefone, DateTime CriadoEm, int Pedidos);
