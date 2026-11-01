using QRCoder;

namespace AtelieMarylu.Web.Integracoes;

// Gera o QR code do "PIX copia e cola" como imagem embutida (data URI), sem chamar serviço externo
public static class PixQrCode
{
    public static string GerarDataUri(string pixCopiaECola)
    {
        using var gerador = new QRCodeGenerator();
        using var dados = gerador.CreateQrCode(pixCopiaECola, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(dados).GetGraphic(8);
        return $"data:image/png;base64,{Convert.ToBase64String(png)}";
    }
}
