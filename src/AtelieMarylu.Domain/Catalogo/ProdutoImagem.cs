namespace AtelieMarylu.Domain.Catalogo;

public class ProdutoImagem
{
    public int Id { get; set; }
    public int ProdutoId { get; set; }
    public string Url { get; set; } = "";
    public string UrlMiniatura { get; set; } = "";
    public int Ordem { get; set; }
}
