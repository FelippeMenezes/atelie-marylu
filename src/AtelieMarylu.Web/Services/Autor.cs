namespace AtelieMarylu.Web.Services;

public record Autor(string? Id, string Nome)
{
    public static readonly Autor Sistema = new(null, "sistema");
}
