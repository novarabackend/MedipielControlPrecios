using Medipiel.Api.Data;
using Medipiel.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Medipiel.Api.Controllers;

[ApiController]
[Route("api/brands")]
public class BrandsController : ControllerBase
{
    private readonly AppDbContext _db;

    public BrandsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var items = await _db.Brands.OrderBy(x => x.Name).ToListAsync();
        return Ok(items);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Brand input)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
        {
            return BadRequest("Name is required.");
        }

        var entity = new Brand { Name = input.Name.Trim() };
        _db.Brands.Add(entity);
        await _db.SaveChangesAsync();
        return Created($"api/brands/{entity.Id}", entity);
    }
}
