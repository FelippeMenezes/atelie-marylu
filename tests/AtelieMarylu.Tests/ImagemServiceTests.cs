using System.Text;
using AtelieMarylu.Web.Services;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace AtelieMarylu.Tests;

public sealed class ImagemServiceTests : IDisposable
{
    private readonly string pasta = Directory.CreateTempSubdirectory("atelie-img-").FullName;
    private readonly ImagemService servico;

    public ImagemServiceTests() => servico = new ImagemService(new AmbienteFalso(pasta), NullLogger<ImagemService>.Instance);

    [Fact]
    public async Task Recusa_arquivo_que_nao_e_imagem_mesmo_com_tipo_de_imagem()
    {
        var falso = new ArquivoFalso(Encoding.UTF8.GetBytes("<script>alert(1)</script>"), "image/png");

        await Assert.ThrowsAsync<InvalidOperationException>(() => servico.SalvarAsync(falso));
        Assert.False(Directory.Exists(Path.Join(pasta, "uploads")) && Directory.EnumerateFiles(Path.Join(pasta, "uploads"), "*", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public async Task Converte_para_webp_reduz_e_gera_miniatura_quadrada()
    {
        var png = CriarPng(2400, 1200);

        var salva = await servico.SalvarAsync(new ArquivoFalso(png, "image/png"));

        using var grande = SKBitmap.Decode(Path.Join(pasta, salva.Url.TrimStart('/')));
        using var mini = SKBitmap.Decode(Path.Join(pasta, salva.UrlMiniatura.TrimStart('/')));
        Assert.EndsWith(".webp", salva.Url);
        Assert.Equal((1600, 800), (grande.Width, grande.Height));
        Assert.Equal((480, 480), (mini.Width, mini.Height));
    }

    [Fact]
    public async Task Excluir_nao_sai_da_pasta_de_uploads()
    {
        var foraDaPasta = Path.Join(pasta, "importante.txt");
        await File.WriteAllTextAsync(foraDaPasta, "não apagar");

        servico.Excluir("/uploads/produtos/../../importante.txt");

        Assert.True(File.Exists(foraDaPasta));
    }

    private static byte[] CriarPng(int largura, int altura)
    {
        using var bitmap = new SKBitmap(largura, altura);
        using (var canvas = new SKCanvas(bitmap)) canvas.Clear(new SKColor(0xCE, 0x47, 0x97));
        using var imagem = SKImage.FromBitmap(bitmap);
        return imagem.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    public void Dispose() => Directory.Delete(pasta, recursive: true);

    private sealed class ArquivoFalso(byte[] conteudo, string tipo) : IBrowserFile
    {
        public string Name => "foto.png";
        public DateTimeOffset LastModified => DateTimeOffset.UtcNow;
        public long Size => conteudo.Length;
        public string ContentType => tipo;
        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default) => new MemoryStream(conteudo);
    }

    private sealed class AmbienteFalso(string raiz) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = raiz;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "Testes";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = raiz;
        public string EnvironmentName { get; set; } = "Testing";
    }
}
