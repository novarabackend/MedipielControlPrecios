using Medipiel.Api.Data;
using Medipiel.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Medipiel.Api.Controllers;

[ApiController]
[Route("api/competitors")]
public class CompetitorsController : ControllerBase
{
    private readonly AppDbContext _db;

    public CompetitorsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var items = await _db.Competitors.OrderBy(x => x.Name).ToListAsync();
        return Ok(items);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CompetitorRequest input)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
        {
            return BadRequest("Name is required.");
        }

        var entity = new Competitor
        {
            Name = input.Name.Trim(),
            BaseUrl = input.BaseUrl,
            AdapterId = input.AdapterId,
            IsActive = input.IsActive ?? true,
        };
        _db.Competitors.Add(entity);
        await _db.SaveChangesAsync();
        return Created($"api/competitors/{entity.Id}", entity);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] CompetitorRequest input)
    {
        var entity = await _db.Competitors.FirstOrDefaultAsync(x => x.Id == id);
        if (entity is null)
        {
            return NotFound();
        }

        if (!string.IsNullOrWhiteSpace(input.Name))
        {
            entity.Name = input.Name.Trim();
        }

        entity.BaseUrl = input.BaseUrl;
        entity.AdapterId = input.AdapterId;
        entity.IsActive = input.IsActive ?? entity.IsActive;

        await _db.SaveChangesAsync();
        return Ok(entity);
    }
}

public sealed record CompetitorRequest(string Name, string? BaseUrl, string? AdapterId, bool? IsActive);
