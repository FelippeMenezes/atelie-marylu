namespace AtelieMarylu.Domain.Shipping;

public static class PostalCode
{
    public static string? Normalize(string? postalCode)
    {
        var digits = new string((postalCode ?? "").Where(char.IsDigit).ToArray());
        return digits.Length == 8 ? digits : null;
    }
}
