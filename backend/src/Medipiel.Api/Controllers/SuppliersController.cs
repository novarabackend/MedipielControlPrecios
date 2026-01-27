using Medipiel.Api.Data;
using Medipiel.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Medipiel.Api.Controllers;

[ApiController]
[Route("api/suppliers")]
public class SuppliersController : ControllerBase
{
    private readonly AppDbContext _db;

    public SuppliersController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var items = await _db.Suppliers.OrderBy(x => x.Name).ToListAsync();
        return Ok(items);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Supplier input)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
        {
            return BadRequest("Name is required.");
        }

        var entity = new Supplier { Name = input.Name.Trim() };
        _db.Suppliers.Add(entity);
        await _db.SaveChangesAsync();
        return Created($"api/suppliers/{entity.Id}", entity);
    }
}
