using Microsoft.AspNetCore.Components.Forms;
using SkiaSharp;

namespace AtelieMarylu.Web.Services;

// Fotos dos produtos, guardadas em wwwroot/uploads/produtos.
// Toda foto é decodificada e regravada do zero (SkiaSharp), o que:
// - garante que é uma imagem de verdade (não confiamos na extensão nem no tipo enviado pelo navegador);
// - remove os metadados EXIF, inclusive a localização GPS de quem tirou a foto;
// - corrige a rotação, reduz o tamanho e gera uma miniatura quadrada para a vitrine.
// Em produção, o ideal é trocar a pasta local por um storage externo (ex.: Cloudflare R2, S3).
public class ImageService(IWebHostEnvironment env, ILogger<ImageService> logger)
{
    public const long MaxFileSize = 8 * 1024 * 1024;
    private const int MaxSide = 1600;
    private const int ThumbnailSide = 480;
    private const long MaxPixels = 40_000_000;
    private const int Quality = 82;

    private static readonly SKSamplingOptions Sampling = new(SKCubicResampler.Mitchell);

    public async Task<SavedImage> SaveAsync(IBrowserFile file)
    {
        using var memory = new MemoryStream();
        await using (var input = file.OpenReadStream(MaxFileSize))
            await input.CopyToAsync(memory);
        memory.Position = 0;

        // Lê só o cabeçalho antes: um arquivo pequeno pode declarar dimensões gigantes ("bomba de descompressão")
        using var codec = SKCodec.Create(memory);
        if (codec is null || codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png or SKEncodedImageFormat.Webp))
        {
            logger.LogWarning("Upload recusado: não é JPG/PNG/WEBP válido");
            throw new InvalidOperationException("O arquivo não é uma imagem válida. Envie JPG, PNG ou WEBP.");
        }
        if ((long)codec.Info.Width * codec.Info.Height > MaxPixels)
            throw new InvalidOperationException("Imagem grande demais.");

        using var decoded = SKBitmap.Decode(codec)
            ?? throw new InvalidOperationException("Não foi possível ler a imagem.");
        using var oriented = ApplyOrientation(decoded, codec.EncodedOrigin);

        var folder = Path.Join(env.WebRootPath, "uploads", "produtos");
        Directory.CreateDirectory(folder);
        var name = Guid.NewGuid().ToString("N");

        using (var large = Resize(oriented, MaxSide))
            WriteFile(large, Path.Join(folder, $"{name}.webp"));
        using (var thumbnail = CropSquare(oriented, ThumbnailSide))
            WriteFile(thumbnail, Path.Join(folder, $"{name}-mini.webp"));

        return new SavedImage($"/uploads/produtos/{name}.webp", $"/uploads/produtos/{name}-mini.webp");
    }

    // Remove os arquivos de uma foto excluída. Só apaga dentro da pasta de uploads.
    public void Delete(params string?[] urls)
    {
        var folder = Path.GetFullPath(Path.Join(env.WebRootPath, "uploads", "produtos"));
        foreach (var url in urls.Where(u => !string.IsNullOrEmpty(u)))
        {
            var path = Path.GetFullPath(Path.Join(env.WebRootPath, url!.TrimStart('/')));
            if (path.StartsWith(folder + Path.DirectorySeparatorChar) && File.Exists(path))
                File.Delete(path);
        }
    }

    // Celulares gravam a foto "deitada" e indicam a rotação no EXIF; aplicamos a rotação nos pixels
    private static SKBitmap ApplyOrientation(SKBitmap source, SKEncodedOrigin orientation)
    {
        if (orientation == SKEncodedOrigin.TopLeft) return source.Copy();

        var swapSides = orientation is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var (w, h) = swapSides ? (source.Height, source.Width) : (source.Width, source.Height);
        var target = new SKBitmap(w, h, source.ColorType, source.AlphaType);
        using var canvas = new SKCanvas(target);

        switch (orientation)
        {
            case SKEncodedOrigin.TopRight: canvas.Scale(-1, 1, w / 2f, 0); break;
            case SKEncodedOrigin.BottomRight: canvas.RotateDegrees(180, w / 2f, h / 2f); break;
            case SKEncodedOrigin.BottomLeft: canvas.Scale(1, -1, 0, h / 2f); break;
            case SKEncodedOrigin.LeftTop: canvas.Translate(w, 0); canvas.RotateDegrees(90); canvas.Scale(1, -1, 0, source.Height / 2f); break;
            case SKEncodedOrigin.RightTop: canvas.Translate(w, 0); canvas.RotateDegrees(90); break;
            case SKEncodedOrigin.RightBottom: canvas.Translate(0, h); canvas.RotateDegrees(-90); canvas.Scale(1, -1, 0, source.Height / 2f); break;
            case SKEncodedOrigin.LeftBottom: canvas.Translate(0, h); canvas.RotateDegrees(-90); break;
        }
        using var image = SKImage.FromBitmap(source);
        canvas.DrawImage(image, 0, 0, Sampling);
        return target;
    }

    private static SKBitmap Resize(SKBitmap source, int maxSide)
    {
        var scale = Math.Min(1f, (float)maxSide / Math.Max(source.Width, source.Height));
        var info = new SKImageInfo(Math.Max(1, (int)(source.Width * scale)), Math.Max(1, (int)(source.Height * scale)));
        return source.Resize(info, Sampling) ?? throw new InvalidOperationException("Falha ao redimensionar a imagem.");
    }

    private static SKBitmap CropSquare(SKBitmap source, int lado)
    {
        var smaller = Math.Min(source.Width, source.Height);
        var cropArea = SKRectI.Create((source.Width - smaller) / 2, (source.Height - smaller) / 2, smaller, smaller);
        var target = new SKBitmap(lado, lado);
        using var canvas = new SKCanvas(target);
        using var image = SKImage.FromBitmap(source);
        canvas.DrawImage(image, cropArea, SKRect.Create(lado, lado), Sampling);
        return target;
    }

    private static void WriteFile(SKBitmap bitmap, string path)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Webp, Quality)
            ?? throw new InvalidOperationException("Falha ao converter a imagem.");
        using var file = File.Create(path);
        data.SaveTo(file);
    }
}
