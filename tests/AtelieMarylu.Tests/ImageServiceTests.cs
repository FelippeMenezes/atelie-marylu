using System.Text;
using AtelieMarylu.Web.Services;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace AtelieMarylu.Tests;

public sealed class ImageServiceTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("atelie-img-").FullName;
    private readonly ImageService service;

    public ImageServiceTests() => service = new ImageService(new FakeEnvironment(folder), NullLogger<ImageService>.Instance);

    [Fact]
    public async Task Rejects_non_image_file_even_with_image_type()
    {
        var falso = new FakeFile(Encoding.UTF8.GetBytes("<script>alert(1)</script>"), "image/png");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(falso));
        Assert.False(Directory.Exists(Path.Join(folder, "uploads")) && Directory.EnumerateFiles(Path.Join(folder, "uploads"), "*", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public async Task Converts_to_webp_resizes_and_creates_square_thumbnail()
    {
        var png = CreatePng(2400, 1200);

        var saved = await service.SaveAsync(new FakeFile(png, "image/png"));

        using var large = SKBitmap.Decode(Path.Join(folder, saved.Url.TrimStart('/')));
        using var thumbnail = SKBitmap.Decode(Path.Join(folder, saved.ThumbnailUrl.TrimStart('/')));
        Assert.EndsWith(".webp", saved.Url);
        Assert.Equal((1600, 800), (large.Width, large.Height));
        Assert.Equal((480, 480), (thumbnail.Width, thumbnail.Height));
    }

    [Fact]
    public async Task Delete_does_not_leave_uploads_folder()
    {
        var outsideFolder = Path.Join(folder, "importante.txt");
        await File.WriteAllTextAsync(outsideFolder, "não apagar");

        service.Delete("/uploads/produtos/../../importante.txt");

        Assert.True(File.Exists(outsideFolder));
    }

    private static byte[] CreatePng(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap)) canvas.Clear(new SKColor(0xCE, 0x47, 0x97));
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private sealed class FakeFile(byte[] content, string type) : IBrowserFile
    {
        public string Name => "foto.png";
        public DateTimeOffset LastModified => DateTimeOffset.UtcNow;
        public long Size => content.Length;
        public string ContentType => type;
        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default) => new MemoryStream(content);
    }

    private sealed class FakeEnvironment(string root) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = root;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "Testes";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Testing";
    }
}
