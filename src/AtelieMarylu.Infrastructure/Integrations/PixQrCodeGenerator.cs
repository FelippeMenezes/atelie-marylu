using QRCoder;

namespace AtelieMarylu.Infrastructure.Integrations;

// Gera o QR code do "PIX copia e cola" como imagem embutida (data URI), sem chamar serviço externo
public static class PixQrCodeGenerator
{
    public static string GenerateDataUri(string pixCopyPaste)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(pixCopyPaste, QRCodeGenerator.ECCLevel.M);
        using var pngGenerator = new PngByteQRCode(data);
        var png = pngGenerator.GetGraphic(8);
        return $"data:image/png;base64,{Convert.ToBase64String(png)}";
    }
}
