namespace Medipiel.Competitors.Abstractions;

public interface ICompetitorProductProbe
{
    Task<CompetitorProductProbeResult> ProbeAsync(CompetitorProductProbeRequest request, CancellationToken ct);
}

public sealed record CompetitorProductProbeRequest(string BaseUrl, string ProductUrl);

public sealed record CompetitorProductProbeResult(
    string Url,
    decimal? ListPrice,
    decimal? PromoPrice,
    string? RawListText,
    string? RawPromoText,
    string? RawSingleAmountText,
    string? DecodedListText,
    string? DecodedPromoText,
    string? DecodedSingleAmountText
);

