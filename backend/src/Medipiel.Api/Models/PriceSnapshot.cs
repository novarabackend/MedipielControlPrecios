namespace Medipiel.Api.Models;

public class PriceSnapshot
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public int CompetitorId { get; set; }
    public Competitor Competitor { get; set; } = null!;

    public DateOnly SnapshotDate { get; set; }
    public decimal? ListPrice { get; set; }
    public decimal? PromoPrice { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
