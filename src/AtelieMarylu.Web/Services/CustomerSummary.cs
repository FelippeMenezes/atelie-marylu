namespace AtelieMarylu.Web.Services;

// Uma linha da lista de clientes do painel
public record CustomerSummary(string Id, string Name, string? Email, string? Phone, DateTime CreatedAt, int Orders);
