namespace AtelieMarylu.Domain.Frete;

public static class Cep
{
    public static string? Normalizar(string? cep)
    {
        var digitos = new string((cep ?? "").Where(char.IsDigit).ToArray());
        return digitos.Length == 8 ? digitos : null;
    }
}
