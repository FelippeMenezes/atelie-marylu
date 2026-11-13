using System.Security.Cryptography;
using System.Text;
using AtelieMarylu.Infrastructure.Integrations;

namespace AtelieMarylu.Tests;

// A assinatura é o que impede alguém de forjar um "pagamento aprovado" no webhook.
// O teste faz o papel do PagBank: gera um par de chaves, assina com a privada e entrega só a pública à loja.
public sealed class PagBankSignatureTests : IDisposable
{
    private readonly ECDsa pagBank = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("""{"id":"ORDE_1","charges":[{"status":"PAID"}]}""");

    public void Dispose() => pagBank.Dispose();

    private byte[] PublicKey => pagBank.ExportSubjectPublicKeyInfo();

    public static string Sign(ECDsa key, byte[] body) =>
        Convert.ToBase64String(key.SignData(body, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));

    [Fact]
    public void Valid_signature_is_accepted()
    {
        Assert.True(PagBankSignature.IsValid(Body, Sign(pagBank, Body), PublicKey));
    }

    [Fact]
    public void Signature_in_p1363_format_is_accepted()
    {
        // O mesmo resultado escrito como os dois números colados (64 bytes), e não em DER
        var p1363 = Convert.ToBase64String(pagBank.SignData(Body, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        Assert.True(PagBankSignature.IsValid(Body, p1363, PublicKey));
    }

    [Fact]
    public void Changed_body_is_refused()
    {
        // Assinatura verdadeira, mas alguém trocou o conteúdo do aviso
        var changed = Encoding.UTF8.GetBytes("""{"id":"ORDE_2","charges":[{"status":"PAID"}]}""");
        Assert.False(PagBankSignature.IsValid(changed, Sign(pagBank, Body), PublicKey));
    }

    [Fact]
    public void Signature_from_another_key_is_refused()
    {
        using var attacker = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.False(PagBankSignature.IsValid(Body, Sign(attacker, Body), PublicKey));
    }

    [Fact]
    public void One_valid_signature_among_several_is_enough()
    {
        // Durante a troca de chave, o PagBank manda mais de uma assinatura separada por vírgula
        using var oldKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var header = $"{Sign(oldKey, Body)}, {Sign(pagBank, Body)}";
        Assert.True(PagBankSignature.IsValid(Body, header, PublicKey));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("não é base64!")]
    [InlineData("AAAA")]
    public void Malformed_header_is_refused(string? header)
    {
        Assert.False(PagBankSignature.IsValid(Body, header, PublicKey));
    }

    [Fact]
    public void Invalid_public_key_is_refused()
    {
        Assert.False(PagBankSignature.IsValid(Body, Sign(pagBank, Body), [1, 2, 3]));
    }
}
