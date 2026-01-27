using Medipiel.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Medipiel.Api.Controllers;

[ApiController]
[Route("api/products")]
public class ProductsController : ControllerBase
{
    private readonly AppDbContext _db;

    public ProductsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int? brandId, [FromQuery] int? categoryId, [FromQuery] string? search)
    {
        var query = _db.Products.AsNoTracking().Include(x => x.Brand).Include(x => x.Category).Include(x => x.Supplier).AsQueryable();

        if (brandId.HasValue)
        {
            query = query.Where(x => x.BrandId == brandId);
        }

        if (categoryId.HasValue)
        {
            query = query.Where(x => x.CategoryId == categoryId);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.Description.Contains(term) || x.Sku.Contains(term) || (x.Ean != null && x.Ean.Contains(term)));
        }

        var items = await query.OrderBy(x => x.Description).ToListAsync();
        return Ok(items);
    }
}
