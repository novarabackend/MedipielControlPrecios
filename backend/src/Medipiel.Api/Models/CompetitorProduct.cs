namespace Medipiel.Api.Models;

public class CompetitorProduct
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public int CompetitorId { get; set; }
    public Competitor Competitor { get; set; } = null!;

    public string? Url { get; set; }
    public string? MatchMethod { get; set; }
    public decimal? MatchScore { get; set; }
    public DateTime? LastMatchedAt { get; set; }
}
