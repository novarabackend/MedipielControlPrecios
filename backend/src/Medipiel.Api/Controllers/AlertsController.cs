using Medipiel.Api.Data;
using Medipiel.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Medipiel.Api.Controllers;

[ApiController]
[Route("api/alerts")]
public class AlertsController : ControllerBase
{
    private readonly AppDbContext _db;

    public AlertsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var items = await (
            from alert in _db.Alerts.AsNoTracking()
            join product in _db.Products.AsNoTracking() on alert.ProductId equals product.Id
            join brand in _db.Brands.AsNoTracking() on product.BrandId equals brand.Id into brandJoin
            from brand in brandJoin.DefaultIfEmpty()
            join competitor in _db.Competitors.AsNoTracking() on alert.CompetitorId equals competitor.Id
            orderby alert.CreatedAt descending
            select new AlertItemDto(
                alert.Id,
                alert.Type,
                alert.Message,
                alert.Status,
                alert.CreatedAt,
                product.Id,
                product.Sku,
                product.Ean,
                product.Description,
                brand != null ? brand.Name : null,
                competitor.Id,
                competitor.Name
            )
        ).ToListAsync();
        return Ok(items);
    }

    [HttpGet("rules")]
    public async Task<IActionResult> GetRules()
    {
        var items = await _db.AlertRules.AsNoTracking()
            .Include(x => x.Brand)
            .OrderBy(x => x.Brand.Name)
            .Select(x => new AlertRuleDto(
                x.Id,
                x.BrandId,
                x.Brand.Name,
                x.ListPriceThresholdPercent,
                x.PromoPriceThresholdPercent,
                x.Active
            ))
            .ToListAsync();

        return Ok(items);
    }

    [HttpPost("rules")]
    public async Task<IActionResult> CreateRule([FromBody] AlertRule input)
    {
        if (input.BrandId == 0)
        {
            return BadRequest("BrandId is required.");
        }

        var rule = new AlertRule
        {
            BrandId = input.BrandId,
            ListPriceThresholdPercent = input.ListPriceThresholdPercent,
            PromoPriceThresholdPercent = input.PromoPriceThresholdPercent,
            Active = input.Active
        };

        _db.AlertRules.Add(rule);
        await _db.SaveChangesAsync();
        return Created($"api/alerts/rules/{rule.Id}", rule);
    }

    [HttpPut("rules/{brandId:int}")]
    public async Task<IActionResult> UpsertRule(int brandId, [FromBody] AlertRuleUpsert input)
    {
        if (brandId <= 0)
        {
            return BadRequest("BrandId is required.");
        }

        var brand = await _db.Brands.FirstOrDefaultAsync(x => x.Id == brandId);
        if (brand is null)
        {
            return BadRequest("Brand not found.");
        }

        var rule = await _db.AlertRules.FirstOrDefaultAsync(x => x.BrandId == brandId);
        if (rule is null)
        {
            rule = new AlertRule
            {
                BrandId = brandId
            };
            _db.AlertRules.Add(rule);
        }

        rule.ListPriceThresholdPercent = input.ListPriceThresholdPercent;
        rule.PromoPriceThresholdPercent = input.PromoPriceThresholdPercent;
        rule.Active = input.Active;

        await _db.SaveChangesAsync();

        return Ok(new AlertRuleDto(
            rule.Id,
            rule.BrandId,
            brand.Name,
            rule.ListPriceThresholdPercent,
            rule.PromoPriceThresholdPercent,
            rule.Active
        ));
    }
}

public record AlertRuleDto(
    int Id,
    int BrandId,
    string BrandName,
    decimal? ListPriceThresholdPercent,
    decimal? PromoPriceThresholdPercent,
    bool Active
);

public record AlertRuleUpsert(
    decimal? ListPriceThresholdPercent,
    decimal? PromoPriceThresholdPercent,
    bool Active
);

public record AlertItemDto(
    int Id,
    string Type,
    string Message,
    string Status,
    DateTime CreatedAt,
    int ProductId,
    string? ProductSku,
    string? ProductEan,
    string ProductDescription,
    string? BrandName,
    int CompetitorId,
    string CompetitorName
);
