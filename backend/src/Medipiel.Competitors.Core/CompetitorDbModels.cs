namespace Medipiel.Competitors.Core;

public sealed record ProductRow(
    int Id,
    string? Ean,
    string Description,
    string? Url,
    string? BrandName
);
