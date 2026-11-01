namespace AtelieMarylu.Domain.Catalogo;

public class Produto
{
    public int Id { get; set; }
    public string Nome { get; set; } = "";
    public string Descricao { get; set; } = "";
    public decimal Preco { get; set; }
    public int Estoque { get; set; }

    // Fotos em ordem de exibição; a primeira é a capa
    public List<ProdutoImagem> Imagens { get; set; } = [];

    // Produto inativo não aparece na vitrine, mas continua nos pedidos antigos
    public bool Ativo { get; set; } = true;

    // Vazio = pronta entrega. Com valor = a peça é feita depois da compra.
    public int? PrazoProducaoDias { get; set; }

    // Peso e medidas da embalagem: obrigatórios para cotar frete nos Correios / Melhor Envio
    public int PesoGramas { get; set; } = 300;
    public int AlturaCm { get; set; } = 5;
    public int LarguraCm { get; set; } = 20;
    public int ComprimentoCm { get; set; } = 25;

    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;

    public bool DisponivelParaVenda => Ativo && Estoque > 0;
    public string Slug => Texto.Slug(Nome);
    public string Url => $"produto/{Id}/{Slug}";
    public ProdutoImagem? Capa => Imagens.OrderBy(i => i.Ordem).FirstOrDefault();
}

public class ProdutoImagem
{
    public int Id { get; set; }
    public int ProdutoId { get; set; }
    public string Url { get; set; } = "";
    public string UrlMiniatura { get; set; } = "";
    public int Ordem { get; set; }
}

