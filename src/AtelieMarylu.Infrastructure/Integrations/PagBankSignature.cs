using System.Security.Cryptography;

namespace AtelieMarylu.Infrastructure.Integrations;

// Confere o cabeçalho x-payload-signature dos avisos do PagBank (documentação: "Validação de autenticidade").
// O PagBank assina o corpo cru do aviso com uma chave PRIVADA (ECDSA + SHA-256) e publica a chave PÚBLICA
// em GET /public-keys/webhook. Quem só tem a pública consegue conferir, mas não consegue assinar.
// Por isso a loja não guarda nenhum segredo para validar o aviso: o token serve só para buscar a chave.
public static class PagBankSignature
{
    // header: uma ou mais assinaturas em Base64 separadas por vírgula (durante a troca de chave,
    // o PagBank manda a antiga e a nova). Basta uma ser válida.
    // publicKey: chave pública no formato X.509 (SubjectPublicKeyInfo), já decodificada do Base64.
    public static bool IsValid(ReadOnlySpan<byte> rawBody, string? header, byte[] publicKey)
    {
        if (string.IsNullOrWhiteSpace(header) || publicKey.Length == 0) return false;

        using var ecdsa = ECDsa.Create();
        try
        {
            ecdsa.ImportSubjectPublicKeyInfo(publicKey, out _);
        }
        catch (CryptographicException)
        {
            return false;
        }

        foreach (var part in header.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            byte[] signature;
            try
            {
                signature = Convert.FromBase64String(part);
            }
            catch (FormatException)
            {
                continue;
            }

            // A assinatura ECDSA são dois números (r e s), que podem ser escritos de dois jeitos: DER (o padrão do
            // Node e do Java, usado nos exemplos oficiais, com 70 a 72 bytes) ou IEEE P1363 (os dois números
            // colados, 64 bytes). A documentação não diz qual o PagBank usa, então os dois são aceitos.
            // Não enfraquece nada: é a mesma chave e a mesma conta, só muda a forma de escrever o resultado.
            if (ecdsa.VerifyData(rawBody, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence)
                || ecdsa.VerifyData(rawBody, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                return true;
        }
        return false;
    }
}
