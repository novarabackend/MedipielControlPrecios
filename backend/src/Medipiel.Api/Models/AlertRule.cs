namespace Medipiel.Api.Models;

public class AlertRule
{
    public int Id { get; set; }
    public int BrandId { get; set; }
    public Brand Brand { get; set; } = null!;

    public decimal? ListPriceThresholdPercent { get; set; }
    public decimal? PromoPriceThresholdPercent { get; set; }
    public bool Active { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
