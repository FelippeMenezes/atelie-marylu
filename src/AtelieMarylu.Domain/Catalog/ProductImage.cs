namespace AtelieMarylu.Domain.Catalog;

public class ProductImage
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string Url { get; set; } = "";
    public string ThumbnailUrl { get; set; } = "";
    public int SortOrder { get; set; }
}
