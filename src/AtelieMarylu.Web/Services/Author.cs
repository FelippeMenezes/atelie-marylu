namespace AtelieMarylu.Web.Services;

public record Author(string? Id, string Name)
{
    public static readonly Author Automated = new(null, "system");
}
