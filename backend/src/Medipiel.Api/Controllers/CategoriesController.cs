using Medipiel.Api.Data;
using Medipiel.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Medipiel.Api.Controllers;

[ApiController]
[Route("api/categories")]
public class CategoriesController : ControllerBase
{
    private readonly AppDbContext _db;

    public CategoriesController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var items = await _db.Categories.OrderBy(x => x.Name).ToListAsync();
        return Ok(items);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Category input)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
        {
            return BadRequest("Name is required.");
        }

        var entity = new Category { Name = input.Name.Trim() };
        _db.Categories.Add(entity);
        await _db.SaveChangesAsync();
        return Created($"api/categories/{entity.Id}", entity);
    }
}
