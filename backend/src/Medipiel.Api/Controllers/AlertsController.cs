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
        var items = await _db.Alerts.AsNoTracking()
            .Include(x => x.Product)
            .Include(x => x.Competitor)
            .OrderByDescending(x => x.CreatedAt)
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
}
