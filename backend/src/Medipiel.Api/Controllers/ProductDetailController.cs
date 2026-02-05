using Medipiel.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Medipiel.Api.Controllers;

[ApiController]
[Route("api/products")]
public class ProductDetailController : ControllerBase
{
    private readonly AppDbContext _db;

    public ProductDetailController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("{id:int}/detail")]
    public async Task<IActionResult> GetDetail(int id, [FromQuery] int? days, CancellationToken ct)
    {
        var product = await (
            from p in _db.Products.AsNoTracking()
            join b in _db.Brands.AsNoTracking() on p.BrandId equals b.Id into bJoin
            from b in bJoin.DefaultIfEmpty()
            join c in _db.Categories.AsNoTracking() on p.CategoryId equals c.Id into cJoin
            from c in cJoin.DefaultIfEmpty()
            join s in _db.Suppliers.AsNoTracking() on p.SupplierId equals s.Id into sJoin
            from s in sJoin.DefaultIfEmpty()
            join l in _db.Lines.AsNoTracking() on p.LineId equals l.Id into lJoin
            from l in lJoin.DefaultIfEmpty()
            where p.Id == id
            select new ProductDetailInfo(
                p.Id,
                p.Sku,
                p.Ean,
                p.Description,
                b != null ? b.Name : null,
                c != null ? c.Name : null,
                s != null ? s.Name : null,
                l != null ? l.Name : null,
                p.MedipielListPrice,
                p.MedipielPromoPrice
            )
        ).FirstOrDefaultAsync(ct);

        if (product is null)
        {
            return NotFound();
        }

        var competitorEntities = await _db.Competitors
            .AsNoTracking()
            .Where(x => x.IsActive)
            .ToListAsync(ct);

        var competitors = competitorEntities
            .OrderBy(x => ResolveOrder(x.Name))
            .ThenBy(x => x.Name)
            .Select(x => new CompetitorInfo(x.Id, x.Name, ResolveColor(x.Name)))
            .ToList();

        var latestDate = await _db.PriceSnapshots
            .Where(x => x.ProductId == id)
            .MaxAsync(x => (DateOnly?)x.SnapshotDate, ct);

        var competitorProducts = await _db.CompetitorProducts
            .AsNoTracking()
            .Where(x => x.ProductId == id)
            .ToListAsync(ct);

        var cpByCompetitor = competitorProducts
            .GroupBy(x => x.CompetitorId)
            .ToDictionary(x => x.Key, x => x.First());

        var latestPrices = new List<ProductCompetitorPrice>();
        if (latestDate is not null)
        {
            var latestSnapshots = await _db.PriceSnapshots
                .AsNoTracking()
                .Where(x => x.ProductId == id && x.SnapshotDate == latestDate.Value)
                .ToListAsync(ct);

            var snapshotByCompetitor = latestSnapshots
                .GroupBy(x => x.CompetitorId)
                .ToDictionary(x => x.Key, x => x.First());

            foreach (var competitor in competitors)
            {
                snapshotByCompetitor.TryGetValue(competitor.Id, out var snapshot);
                cpByCompetitor.TryGetValue(competitor.Id, out var cp);

                var diffList = ComputeDiff(product.MedipielListPrice, snapshot?.ListPrice);
                var diffPromo = ComputeDiff(product.MedipielPromoPrice, snapshot?.PromoPrice);

                latestPrices.Add(new ProductCompetitorPrice(
                    competitor.Id,
                    snapshot?.ListPrice,
                    snapshot?.PromoPrice,
                    cp?.Url,
                    cp?.MatchMethod,
                    cp?.MatchScore,
                    cp?.LastMatchedAt,
                    diffList,
                    diffPromo
                ));
            }
        }

        var history = new List<ProductHistoryPoint>();
        if (latestDate is not null)
        {
            var historyDays = Math.Clamp(days ?? 7, 1, 60);
            var fromDate = latestDate.Value.AddDays(-(historyDays - 1));

            var historySnapshots = await _db.PriceSnapshots
                .AsNoTracking()
                .Where(x => x.ProductId == id && x.SnapshotDate >= fromDate && x.SnapshotDate <= latestDate.Value)
                .ToListAsync(ct);

            history = historySnapshots
                .GroupBy(x => x.SnapshotDate)
                .OrderBy(x => x.Key)
                .Select(group => new ProductHistoryPoint(
                    group.Key,
                    group.Select(item => new ProductHistoryPrice(
                        item.CompetitorId,
                        item.ListPrice,
                        item.PromoPrice,
                        ComputeDiff(product.MedipielListPrice, item.ListPrice),
                        ComputeDiff(product.MedipielPromoPrice, item.PromoPrice)
                    )).ToList()
                ))
                .ToList();
        }

        var response = new ProductDetailResponse(
            product,
            latestDate,
            competitors,
            latestPrices,
            history
        );

        return Ok(response);
    }

    [HttpPut("{id:int}/competitors/{competitorId:int}/url")]
    public async Task<IActionResult> UpdateUrl(int id, int competitorId, [FromBody] UpdateCompetitorUrlRequest input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Url))
        {
            return BadRequest("Url is required.");
        }

        var productExists = await _db.Products.AnyAsync(x => x.Id == id, ct);
        if (!productExists)
        {
            return NotFound("Producto no encontrado.");
        }

        var competitorExists = await _db.Competitors.AnyAsync(x => x.Id == competitorId, ct);
        if (!competitorExists)
        {
            return NotFound("Competidor no encontrado.");
        }

        var entity = await _db.CompetitorProducts
            .FirstOrDefaultAsync(x => x.ProductId == id && x.CompetitorId == competitorId, ct);

        if (entity is null)
        {
            entity = new Models.CompetitorProduct
            {
                ProductId = id,
                CompetitorId = competitorId
            };
            _db.CompetitorProducts.Add(entity);
        }

        entity.Url = input.Url.Trim();
        entity.MatchMethod = "manual";
        entity.MatchScore = 1;
        entity.LastMatchedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Ok(new
        {
            entity.ProductId,
            entity.CompetitorId,
            entity.Url,
            entity.MatchMethod,
            entity.MatchScore,
            entity.LastMatchedAt
        });
    }

    private static decimal? ComputeDiff(decimal? basePrice, decimal? competitorPrice)
    {
        if (basePrice is null || basePrice == 0 || competitorPrice is null)
        {
            return null;
        }

        return (competitorPrice - basePrice) / basePrice;
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

public sealed record ProductDetailResponse(
    ProductDetailInfo Product,
    DateOnly? SnapshotDate,
    List<CompetitorInfo> Competitors,
    List<ProductCompetitorPrice> Latest,
    List<ProductHistoryPoint> History
);

public sealed record ProductDetailInfo(
    int Id,
    string? Sku,
    string? Ean,
    string Description,
    string? Brand,
    string? Category,
    string? Supplier,
    string? Line,
    decimal? MedipielListPrice,
    decimal? MedipielPromoPrice
);

public sealed record ProductCompetitorPrice(
    int CompetitorId,
    decimal? ListPrice,
    decimal? PromoPrice,
    string? Url,
    string? MatchMethod,
    decimal? MatchScore,
    DateTime? LastMatchedAt,
    decimal? DiffList,
    decimal? DiffPromo
);

public sealed record ProductHistoryPoint(
    DateOnly Date,
    List<ProductHistoryPrice> Prices
);

public sealed record ProductHistoryPrice(
    int CompetitorId,
    decimal? ListPrice,
    decimal? PromoPrice,
    decimal? DiffList,
    decimal? DiffPromo
);

public sealed record UpdateCompetitorUrlRequest(string Url);
