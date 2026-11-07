namespace AtelieMarylu.Web.Services;

// Só o que as telas do painel mostram. Hash de senha e CPF não saem do banco à toa.
public record CustomerData(string Id, string FullName, string? Email, string? Phone, DateTime CreatedAt)
{
    public string? DisplayName => string.IsNullOrEmpty(FullName) ? Email : FullName;
}
