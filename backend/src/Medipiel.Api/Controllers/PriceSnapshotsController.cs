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

        var response = await BuildSnapshotResponse(latestDate, take, ct);
        return Ok(response);
    }

    [HttpGet("by-date")]
    public async Task<IActionResult> GetByDate([FromQuery] DateOnly? date, [FromQuery] int? take, CancellationToken ct)
    {
        if (date is null)
        {
            return BadRequest("date is required (YYYY-MM-DD).");
        }

        var response = await BuildSnapshotResponse(date.Value, take, ct);
        return Ok(response);
    }

    private async Task<LatestSnapshotPivotResponse> BuildSnapshotResponse(
        DateOnly? snapshotDate,
        int? take,
        CancellationToken ct)
    {
        var competitors = await _db.Competitors
            .AsNoTracking()
            .Where(x => x.IsActive)
            .ToListAsync(ct);

        var orderedCompetitors = competitors
            .OrderBy(x => ResolveOrder(x.AdapterId, x.Name))
            .ThenBy(x => x.Name)
            .Select(x => new CompetitorInfo(x.Id, x.Name, ResolveColor(x.Name)))
            .ToList();

        var limit = take.GetValueOrDefault(0);
        if (limit < 0)
        {
            limit = 0;
        }

        var productsQuery =
            from p in _db.Products.AsNoTracking()
            join b in _db.Brands.AsNoTracking() on p.BrandId equals b.Id into bJoin
            from b in bJoin.DefaultIfEmpty()
            join s in _db.Suppliers.AsNoTracking() on p.SupplierId equals s.Id into sJoin
            from s in sJoin.DefaultIfEmpty()
            join c in _db.Categories.AsNoTracking() on p.CategoryId equals c.Id into cJoin
            from c in cJoin.DefaultIfEmpty()
            join l in _db.Lines.AsNoTracking() on p.LineId equals l.Id into lJoin
            from l in lJoin.DefaultIfEmpty()
            orderby p.Description
            select new
            {
                p.Id,
                p.Sku,
                p.Ean,
                p.Description,
                p.BrandId,
                p.SupplierId,
                p.CategoryId,
                p.LineId,
                BrandName = b != null ? b.Name : null,
                SupplierName = s != null ? s.Name : null,
                CategoryName = c != null ? c.Name : null,
                LineName = l != null ? l.Name : null,
                p.MedipielListPrice,
                p.MedipielPromoPrice
            };

        if (limit > 0)
        {
            productsQuery = productsQuery.Take(limit);
        }

        var products = await productsQuery.ToListAsync(ct);
        if (products.Count == 0)
        {
            return new LatestSnapshotPivotResponse(snapshotDate, orderedCompetitors, new List<SnapshotRow>());
        }

        var productIds = products.Select(p => p.Id).ToList();
        var competitorIds = orderedCompetitors.Select(c => c.Id).ToList();

        var snapshots = snapshotDate is null
            ? new List<SnapshotProjection>()
            : await _db.PriceSnapshots.AsNoTracking()
                .Where(ps => ps.SnapshotDate == snapshotDate.Value)
                .Where(ps => productIds.Contains(ps.ProductId))
                .Where(ps => competitorIds.Contains(ps.CompetitorId))
                .Select(ps => new SnapshotProjection(
                    ps.ProductId,
                    ps.CompetitorId,
                    ps.ListPrice,
                    ps.PromoPrice
                ))
                .ToListAsync(ct);

        var mappings = await _db.CompetitorProducts.AsNoTracking()
            .Where(cp => productIds.Contains(cp.ProductId))
            .Where(cp => competitorIds.Contains(cp.CompetitorId))
            .Select(cp => new { cp.ProductId, cp.CompetitorId, cp.Url, cp.MatchMethod })
            .ToListAsync(ct);

        var priceByProductCompetitor = snapshots.ToDictionary(
            s => (s.ProductId, s.CompetitorId),
            s => new { s.ListPrice, s.PromoPrice }
        );

        var mappingByProductCompetitor = mappings.ToDictionary(
            m => (m.ProductId, m.CompetitorId),
            m => new { m.Url, m.MatchMethod }
        );

        var rows = products
            .Select(product => new SnapshotRow(
                product.Id,
                product.Sku,
                product.Ean,
                product.Description,
                product.BrandId,
                product.SupplierId,
                product.CategoryId,
                product.LineId,
                product.BrandName,
                product.SupplierName,
                product.CategoryName,
                product.LineName,
                product.MedipielListPrice,
                product.MedipielPromoPrice,
                orderedCompetitors.Select(competitor =>
                {
                    var key = (product.Id, competitor.Id);
                    var hasPrice = priceByProductCompetitor.TryGetValue(key, out var price);
                    mappingByProductCompetitor.TryGetValue(key, out var mapping);

                    return new SnapshotPrice(
                        competitor.Id,
                        hasPrice ? price!.ListPrice : null,
                        hasPrice ? price!.PromoPrice : null,
                        mapping?.Url,
                        mapping?.MatchMethod
                    );
                }).ToList()
            ))
            .ToList();

        return new LatestSnapshotPivotResponse(snapshotDate, orderedCompetitors, rows);
    }

    private static string? ResolveColor(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var normalized = name.Trim().ToLowerInvariant();
        if (normalized.Contains("medipiel"))
        {
            return "#a1c9f1";
        }

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

    private static int ResolveOrder(string? adapterId, string name)
    {
        if (!string.IsNullOrWhiteSpace(adapterId) &&
            adapterId.Trim().Equals("medipiel", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

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
    int? BrandId,
    int? SupplierId,
    int? CategoryId,
    int? LineId,
    string? BrandName,
    string? SupplierName,
    string? CategoryName,
    string? LineName,
    decimal? MedipielListPrice,
    decimal? MedipielPromoPrice,
    List<SnapshotPrice> Prices
);

public sealed record SnapshotPrice(
    int CompetitorId,
    decimal? ListPrice,
    decimal? PromoPrice,
    string? Url,
    string? MatchMethod
);

public sealed record SnapshotProjection(
    int ProductId,
    int CompetitorId,
    decimal? ListPrice,
    decimal? PromoPrice
);
