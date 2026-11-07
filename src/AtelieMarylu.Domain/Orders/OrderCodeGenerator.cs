using System.Security.Cryptography;

namespace AtelieMarylu.Domain.Orders;

public static class OrderCodeGenerator
{
    // Sem 0/O e 1/I/L para o cliente não confundir ao ditar o código
    private const string Alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

    // Código público do pedido (ex.: AML-7K3F9Q). Não revela quantos pedidos a loja já teve.
    public static string Create()
    {
        Span<char> code = stackalloc char[6];
        for (var i = 0; i < code.Length; i++)
            code[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return $"AML-{code}";
    }
}
