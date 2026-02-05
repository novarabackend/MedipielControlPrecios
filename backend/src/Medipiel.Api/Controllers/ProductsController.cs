using Medipiel.Api.Data;
using Medipiel.Api.Models;
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
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var items = await (
                from p in _db.Products.AsNoTracking()
                join b in _db.Brands.AsNoTracking() on p.BrandId equals b.Id into bJoin
                from b in bJoin.DefaultIfEmpty()
                join s in _db.Suppliers.AsNoTracking() on p.SupplierId equals s.Id into sJoin
                from s in sJoin.DefaultIfEmpty()
                join c in _db.Categories.AsNoTracking() on p.CategoryId equals c.Id into cJoin
                from c in cJoin.DefaultIfEmpty()
                join l in _db.Lines.AsNoTracking() on p.LineId equals l.Id into lJoin
                from l in lJoin.DefaultIfEmpty()
                select new
                {
                    p.Id,
                    p.Sku,
                    p.Ean,
                    p.Description,
                    p.BrandId,
                    p.SupplierId,
                    p.CategoryId,
                    p.LineId,
                    BrandName = b != null ? b.Name : null,
                    SupplierName = s != null ? s.Name : null,
                    CategoryName = c != null ? c.Name : null,
                    LineName = l != null ? l.Name : null,
                    p.MedipielListPrice,
                    p.MedipielPromoPrice,
                })
            .ToListAsync(ct);

        return Ok(items);
    }

    [HttpPost("import")]
    public async Task<IActionResult> Import([FromBody] ProductImportRequest request, CancellationToken ct)
    {
        if (request.Items.Count == 0)
        {
            return BadRequest("No se recibieron productos.");
        }

        var summary = new ImportSummary();

        var brandCache = await _db.Brands.AsNoTracking().ToDictionaryAsync(x => x.Name, x => x, StringComparer.OrdinalIgnoreCase, ct);
        var supplierCache = await _db.Suppliers.AsNoTracking().ToDictionaryAsync(x => x.Name, x => x, StringComparer.OrdinalIgnoreCase, ct);
        var categoryCache = await _db.Categories.AsNoTracking().ToDictionaryAsync(x => x.Name, x => x, StringComparer.OrdinalIgnoreCase, ct);
        var lineCache = await _db.Lines.AsNoTracking().ToDictionaryAsync(x => x.Name, x => x, StringComparer.OrdinalIgnoreCase, ct);

        var seenEans = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var requestEans = request.Items
            .Select(item => item.Ean?.Trim())
            .Where(ean => !string.IsNullOrWhiteSpace(ean))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var existingProducts = await _db.Products
            .Where(x => x.Ean != null && requestEans.Contains(x.Ean))
            .ToDictionaryAsync(x => x.Ean!, StringComparer.OrdinalIgnoreCase, ct);

        foreach (var item in request.Items)
        {
            summary.Total += 1;

            if (string.IsNullOrWhiteSpace(item.Ean))
            {
                summary.Failed += 1;
                summary.Errors.Add(new ImportError(item.RowNumber, item.Sku, item.Ean, "EAN requerido."));
                continue;
            }

            var sku = item.Sku?.Trim();
            var ean = item.Ean?.Trim();
            var description = item.Description?.Trim() ?? string.Empty;

            var brandId = ResolveMasterId(item.Brand, brandCache);
            var supplierId = ResolveMasterId(item.Supplier, supplierCache);
            var categoryId = ResolveMasterId(item.Category, categoryCache);
            var lineId = ResolveMasterId(item.Line, lineCache);
            var medipielListPrice = item.MedipielListPrice;
            var medipielPromoPrice = item.MedipielPromoPrice;

            if (IsInvalidReference(item.Brand, brandId))
            {
                summary.Failed += 1;
                summary.Errors.Add(new ImportError(item.RowNumber, item.Sku, item.Ean, $"Marca no existe: {item.Brand}"));
                continue;
            }

            if (IsInvalidReference(item.Supplier, supplierId))
            {
                summary.Failed += 1;
                summary.Errors.Add(new ImportError(item.RowNumber, item.Sku, item.Ean, $"Proveedor no existe: {item.Supplier}"));
                continue;
            }

            if (IsInvalidReference(item.Category, categoryId))
            {
                summary.Failed += 1;
                summary.Errors.Add(new ImportError(item.RowNumber, item.Sku, item.Ean, $"Categoria no existe: {item.Category}"));
                continue;
            }

            if (IsInvalidReference(item.Line, lineId))
            {
                summary.Failed += 1;
                summary.Errors.Add(new ImportError(item.RowNumber, item.Sku, item.Ean, $"Linea no existe: {item.Line}"));
                continue;
            }

            if (!string.IsNullOrWhiteSpace(ean) && !seenEans.Add(ean))
            {
                summary.Failed += 1;
                summary.Errors.Add(new ImportError(item.RowNumber, item.Sku, item.Ean, $"EAN duplicado en el archivo: {ean}"));
                continue;
            }

            Product? product = null;
            if (!string.IsNullOrWhiteSpace(ean))
            {
                existingProducts.TryGetValue(ean, out product);
            }

            if (product is null)
            {
                product = new Product
                {
                    Sku = sku,
                    Ean = ean,
                    Description = description,
                    BrandId = brandId,
                    SupplierId = supplierId,
                    CategoryId = categoryId,
                    LineId = lineId,
                    MedipielListPrice = medipielListPrice,
                    MedipielPromoPrice = medipielPromoPrice,
                };
                _db.Products.Add(product);
                if (!string.IsNullOrWhiteSpace(ean))
                {
                    existingProducts[ean] = product;
                }
                summary.Created += 1;
            }
            else
            {
                product.Sku = sku ?? product.Sku;
                product.Ean = ean;
                product.Description = description;
                product.BrandId = brandId;
                product.SupplierId = supplierId;
                product.CategoryId = categoryId;
                product.LineId = lineId;
                product.MedipielListPrice = medipielListPrice;
                product.MedipielPromoPrice = medipielPromoPrice;
                summary.Updated += 1;
            }
        }

        await _db.SaveChangesAsync(ct);
        return Ok(summary);
    }

    private static int? ResolveMasterId<T>(string? rawName, Dictionary<string, T> cache)
        where T : class
    {
        var name = rawName?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        if (cache.TryGetValue(name, out var existing))
        {
            return GetId(existing);
        }

        return null;
    }

    private static bool IsInvalidReference(string? rawName, int? id)
    {
        return !string.IsNullOrWhiteSpace(rawName) && id is null;
    }

    private static int? GetId<T>(T entity)
    {
        var prop = typeof(T).GetProperty("Id");
        if (prop is null)
        {
            return null;
        }
        var value = prop.GetValue(entity);
        if (value is null)
        {
            return null;
        }
        return Convert.ToInt32(value);
    }
}

public sealed record ProductImportRequest(List<ProductImportItem> Items);

public sealed record ProductImportItem(
    int RowNumber,
    string? Sku,
    string? Ean,
    string? Description,
    string? Brand,
    string? Supplier,
    string? Category,
    string? Line,
    decimal? MedipielListPrice,
    decimal? MedipielPromoPrice
);

public sealed record ImportError(int RowNumber, string? Sku, string? Ean, string Message);

public sealed class ImportSummary
{
    public int Total { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Failed { get; set; }
    public List<ImportError> Errors { get; set; } = new();
}
