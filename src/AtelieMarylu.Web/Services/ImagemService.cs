using Microsoft.AspNetCore.Components.Forms;
using SkiaSharp;

namespace AtelieMarylu.Web.Services;

public record ImagemSalva(string Url, string UrlMiniatura);

// Fotos dos produtos, guardadas em wwwroot/uploads/produtos.
// Toda foto é decodificada e regravada do zero (SkiaSharp), o que:
// - garante que é uma imagem de verdade (não confiamos na extensão nem no tipo enviado pelo navegador);
// - remove os metadados EXIF, inclusive a localização GPS de quem tirou a foto;
// - corrige a rotação, reduz o tamanho e gera uma miniatura quadrada para a vitrine.
// Em produção, o ideal é trocar a pasta local por um storage externo (ex.: Cloudflare R2, S3).
public class ImagemService(IWebHostEnvironment env, ILogger<ImagemService> logger)
{
    public const long TamanhoMaximo = 8 * 1024 * 1024;
    private const int LadoMaximo = 1600;
    private const int LadoMiniatura = 480;
    private const long PixelsMaximos = 40_000_000;
    private const int Qualidade = 82;

    private static readonly SKSamplingOptions Amostragem = new(SKCubicResampler.Mitchell);

    public async Task<ImagemSalva> SalvarAsync(IBrowserFile arquivo)
    {
        using var memoria = new MemoryStream();
        await using (var entrada = arquivo.OpenReadStream(TamanhoMaximo))
            await entrada.CopyToAsync(memoria);
        memoria.Position = 0;

        // Lê só o cabeçalho antes: um arquivo pequeno pode declarar dimensões gigantes ("bomba de descompressão")
        using var codec = SKCodec.Create(memoria);
        if (codec is null || codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png or SKEncodedImageFormat.Webp))
        {
            logger.LogWarning("Upload recusado: não é JPG/PNG/WEBP válido");
            throw new InvalidOperationException("O arquivo não é uma imagem válida. Envie JPG, PNG ou WEBP.");
        }
        if ((long)codec.Info.Width * codec.Info.Height > PixelsMaximos)
            throw new InvalidOperationException("Imagem grande demais.");

        using var decodificada = SKBitmap.Decode(codec)
            ?? throw new InvalidOperationException("Não foi possível ler a imagem.");
        using var orientada = Orientar(decodificada, codec.EncodedOrigin);

        var pasta = Path.Combine(env.WebRootPath, "uploads", "produtos");
        Directory.CreateDirectory(pasta);
        var nome = Guid.NewGuid().ToString("N");

        using (var grande = Redimensionar(orientada, LadoMaximo))
            Gravar(grande, Path.Combine(pasta, $"{nome}.webp"));
        using (var mini = RecortarQuadrado(orientada, LadoMiniatura))
            Gravar(mini, Path.Combine(pasta, $"{nome}-mini.webp"));

        return new ImagemSalva($"/uploads/produtos/{nome}.webp", $"/uploads/produtos/{nome}-mini.webp");
    }

    // Remove os arquivos de uma foto excluída. Só apaga dentro da pasta de uploads.
    public void Excluir(params string?[] urls)
    {
        var pasta = Path.GetFullPath(Path.Combine(env.WebRootPath, "uploads", "produtos"));
        foreach (var url in urls)
        {
            if (string.IsNullOrEmpty(url)) continue;
            var caminho = Path.GetFullPath(Path.Combine(env.WebRootPath, url.TrimStart('/')));
            if (caminho.StartsWith(pasta + Path.DirectorySeparatorChar) && File.Exists(caminho))
                File.Delete(caminho);
        }
    }

    // Celulares gravam a foto "deitada" e indicam a rotação no EXIF; aplicamos a rotação nos pixels
    private static SKBitmap Orientar(SKBitmap origem, SKEncodedOrigin orientacao)
    {
        if (orientacao == SKEncodedOrigin.TopLeft) return origem.Copy();

        var trocaLados = orientacao is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var (w, h) = trocaLados ? (origem.Height, origem.Width) : (origem.Width, origem.Height);
        var destino = new SKBitmap(w, h, origem.ColorType, origem.AlphaType);
        using var canvas = new SKCanvas(destino);

        switch (orientacao)
        {
            case SKEncodedOrigin.TopRight: canvas.Scale(-1, 1, w / 2f, 0); break;
            case SKEncodedOrigin.BottomRight: canvas.RotateDegrees(180, w / 2f, h / 2f); break;
            case SKEncodedOrigin.BottomLeft: canvas.Scale(1, -1, 0, h / 2f); break;
            case SKEncodedOrigin.LeftTop: canvas.Translate(w, 0); canvas.RotateDegrees(90); canvas.Scale(1, -1, 0, origem.Height / 2f); break;
            case SKEncodedOrigin.RightTop: canvas.Translate(w, 0); canvas.RotateDegrees(90); break;
            case SKEncodedOrigin.RightBottom: canvas.Translate(0, h); canvas.RotateDegrees(-90); canvas.Scale(1, -1, 0, origem.Height / 2f); break;
            case SKEncodedOrigin.LeftBottom: canvas.Translate(0, h); canvas.RotateDegrees(-90); break;
        }
        using var imagem = SKImage.FromBitmap(origem);
        canvas.DrawImage(imagem, 0, 0, Amostragem);
        return destino;
    }

    private static SKBitmap Redimensionar(SKBitmap origem, int ladoMaximo)
    {
        var escala = Math.Min(1f, (float)ladoMaximo / Math.Max(origem.Width, origem.Height));
        var info = new SKImageInfo(Math.Max(1, (int)(origem.Width * escala)), Math.Max(1, (int)(origem.Height * escala)));
        return origem.Resize(info, Amostragem) ?? throw new InvalidOperationException("Falha ao redimensionar a imagem.");
    }

    private static SKBitmap RecortarQuadrado(SKBitmap origem, int lado)
    {
        var menor = Math.Min(origem.Width, origem.Height);
        var recorte = SKRectI.Create((origem.Width - menor) / 2, (origem.Height - menor) / 2, menor, menor);
        var destino = new SKBitmap(lado, lado);
        using var canvas = new SKCanvas(destino);
        using var imagem = SKImage.FromBitmap(origem);
        canvas.DrawImage(imagem, recorte, SKRect.Create(lado, lado), Amostragem);
        return destino;
    }

    private static void Gravar(SKBitmap bitmap, string caminho)
    {
        using var imagem = SKImage.FromBitmap(bitmap);
        using var dados = imagem.Encode(SKEncodedImageFormat.Webp, Qualidade)
            ?? throw new InvalidOperationException("Falha ao converter a imagem.");
        using var arquivo = File.Create(caminho);
        dados.SaveTo(arquivo);
    }
}
