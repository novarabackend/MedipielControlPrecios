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

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var item = await _db.Brands.FindAsync(id);
        return item is null ? NotFound() : Ok(item);
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

        if (input.SupplierId.HasValue)
        {
            var supplierExists = await _db.Suppliers.AnyAsync(x => x.Id == input.SupplierId.Value);
            if (!supplierExists)
            {
                return BadRequest("Supplier not found.");
            }
        }

        var entity = new Brand { Name = input.Name.Trim(), SupplierId = input.SupplierId };
        _db.Brands.Add(entity);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Conflict("Brand already exists.");
        }
        return Created($"api/brands/{entity.Id}", entity);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] Brand input)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
        {
            return BadRequest("Name is required.");
        }

        var entity = await _db.Brands.FindAsync(id);
        if (entity is null)
        {
            return NotFound();
        }

        if (input.SupplierId.HasValue)
        {
            var supplierExists = await _db.Suppliers.AnyAsync(x => x.Id == input.SupplierId.Value);
            if (!supplierExists)
            {
                return BadRequest("Supplier not found.");
            }
        }

        entity.Name = input.Name.Trim();
        entity.SupplierId = input.SupplierId;
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Conflict("Brand already exists.");
        }

        return Ok(entity);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var entity = await _db.Brands.FindAsync(id);
        if (entity is null)
        {
            return NotFound();
        }

        _db.Brands.Remove(entity);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Conflict("Brand is in use.");
        }

        return NoContent();
    }
}
