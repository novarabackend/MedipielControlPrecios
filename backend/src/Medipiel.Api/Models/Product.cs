namespace Medipiel.Api.Models;

public class Product
{
    public int Id { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string? Ean { get; set; }
    public string Description { get; set; } = string.Empty;

    public int? BrandId { get; set; }
    public Brand? Brand { get; set; }

    public int? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public int? CategoryId { get; set; }
    public Category? Category { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
