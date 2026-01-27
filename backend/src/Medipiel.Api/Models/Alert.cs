namespace Medipiel.Api.Models;

public class Alert
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public int CompetitorId { get; set; }
    public Competitor Competitor { get; set; } = null!;

    public int? AlertRuleId { get; set; }
    public AlertRule? AlertRule { get; set; }

    public string Type { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "open";
}
