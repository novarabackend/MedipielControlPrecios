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
    public async Task<IActionResult> Create([FromBody] Competitor input)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
        {
            return BadRequest("Name is required.");
        }

        var entity = new Competitor { Name = input.Name.Trim(), BaseUrl = input.BaseUrl };
        _db.Competitors.Add(entity);
        await _db.SaveChangesAsync();
        return Created($"api/competitors/{entity.Id}", entity);
    }
}
