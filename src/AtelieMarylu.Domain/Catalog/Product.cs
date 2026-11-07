namespace AtelieMarylu.Domain.Catalog;

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Price { get; set; }
    public int Stock { get; set; }

    // Fotos em ordem de exibição; a primeira é a capa
    public List<ProductImage> Images { get; set; } = [];

    // Produto inativo não aparece na vitrine, mas continua nos pedidos antigos
    public bool IsActive { get; set; } = true;

    // Vazio = pronta entrega. Com valor = a peça é feita depois da compra.
    public int? ProductionDays { get; set; }

    // Peso e medidas da embalagem: obrigatórios para cotar frete nos Correios / Melhor Envio
    public int WeightGrams { get; set; } = 300;
    public int HeightCm { get; set; } = 5;
    public int WidthCm { get; set; } = 20;
    public int LengthCm { get; set; } = 25;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsAvailableForSale => IsActive && Stock > 0;
    public string Slug => SlugGenerator.Create(Name);
    public string Url => $"produto/{Id}/{Slug}";
    public ProductImage? Cover => Images.OrderBy(i => i.SortOrder).FirstOrDefault();
}
