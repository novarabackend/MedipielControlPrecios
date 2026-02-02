using Medipiel.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Medipiel.Api.Controllers;

[ApiController]
[Route("api/price-snapshots")]
public class PriceSnapshotsController : ControllerBase
{
    private readonly AppDbContext _db;

    public PriceSnapshotsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("latest")]
    public async Task<IActionResult> GetLatest([FromQuery] int? take, CancellationToken ct)
    {
        var latestDate = await _db.PriceSnapshots
            .MaxAsync(x => (DateOnly?)x.SnapshotDate, ct);

        var competitors = await _db.Competitors
            .AsNoTracking()
            .Where(x => x.IsActive)
            .ToListAsync(ct);

        var orderedCompetitors = competitors
            .OrderBy(x => ResolveOrder(x.Name))
            .ThenBy(x => x.Name)
            .Select(x => new CompetitorInfo(x.Id, x.Name, ResolveColor(x.Name)))
            .ToList();

        if (latestDate is null)
        {
            return Ok(new LatestSnapshotPivotResponse(null, orderedCompetitors, new List<SnapshotRow>()));
        }

        var limit = take.GetValueOrDefault(200);
        if (limit <= 0)
        {
            limit = 200;
        }

        var flat = await (
            from ps in _db.PriceSnapshots.AsNoTracking()
            join p in _db.Products.AsNoTracking() on ps.ProductId equals p.Id
            join c in _db.Competitors.AsNoTracking() on ps.CompetitorId equals c.Id
            join cp in _db.CompetitorProducts.AsNoTracking()
                on new { ps.ProductId, ps.CompetitorId } equals new { cp.ProductId, cp.CompetitorId }
                into cpJoin
            from cp in cpJoin.DefaultIfEmpty()
            where ps.SnapshotDate == latestDate.Value
            select new SnapshotFlat(
                p.Id,
                p.Sku,
                p.Ean,
                p.Description,
                p.MedipielListPrice,
                p.MedipielPromoPrice,
                c.Id,
                ps.SnapshotDate,
                ps.ListPrice,
                ps.PromoPrice,
                cp.Url
            )
        ).ToListAsync(ct);

        var rows = flat
            .GroupBy(item => new
            {
                item.ProductId,
                item.Sku,
                item.Ean,
                item.Description,
                item.MedipielListPrice,
                item.MedipielPromoPrice
            })
            .Select(group => new SnapshotRow(
                group.Key.ProductId,
                group.Key.Sku,
                group.Key.Ean,
                group.Key.Description,
                group.Key.MedipielListPrice,
                group.Key.MedipielPromoPrice,
                group.Select(item => new SnapshotPrice(
                    item.CompetitorId,
                    item.ListPrice,
                    item.PromoPrice,
                    item.Url
                )).ToList()
            ))
            .OrderBy(row => row.Description)
            .Take(limit)
            .ToList();

        return Ok(new LatestSnapshotPivotResponse(latestDate, orderedCompetitors, rows));
    }

    private static string? ResolveColor(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var normalized = name.Trim().ToLowerInvariant();
        if (normalized.Contains("bella piel"))
        {
            return "#729fcf";
        }

        if (normalized.Contains("linea estetica"))
        {
            return "#ffd9b3";
        }

        if (normalized.Contains("farmatodo"))
        {
            return "#b7d36b";
        }

        if (normalized.Contains("cruz verde"))
        {
            return "#f3a3a3";
        }

        return null;
    }

    private static int ResolveOrder(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return 999;
        }

        var normalized = name.Trim().ToLowerInvariant();
        if (normalized.Contains("bella piel"))
        {
            return 1;
        }

        if (normalized.Contains("linea estetica"))
        {
            return 2;
        }

        if (normalized.Contains("farmatodo"))
        {
            return 3;
        }

        if (normalized.Contains("cruz verde"))
        {
            return 4;
        }

        return 99;
    }
}

public sealed record LatestSnapshotPivotResponse(
    DateOnly? SnapshotDate,
    List<CompetitorInfo> Competitors,
    List<SnapshotRow> Rows
);

public sealed record CompetitorInfo(int Id, string Name, string? Color);

public sealed record SnapshotRow(
    int ProductId,
    string? Sku,
    string? Ean,
    string Description,
    decimal? MedipielListPrice,
    decimal? MedipielPromoPrice,
    List<SnapshotPrice> Prices
);

public sealed record SnapshotPrice(
    int CompetitorId,
    decimal? ListPrice,
    decimal? PromoPrice,
    string? Url
);

public sealed record SnapshotFlat(
    int ProductId,
    string? Sku,
    string? Ean,
    string Description,
    decimal? MedipielListPrice,
    decimal? MedipielPromoPrice,
    int CompetitorId,
    DateOnly SnapshotDate,
    decimal? ListPrice,
    decimal? PromoPrice,
    string? Url
);
