using Medipiel.Api.Data;
using Medipiel.Api.Services;
using Medipiel.Competitors.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace Medipiel.Api.Controllers;

[ApiController]
[Route("api/competitors")]
public sealed class CompetitorProbeController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly CompetitorAdapterRegistry _registry;

    public CompetitorProbeController(AppDbContext db, CompetitorAdapterRegistry registry)
    {
        _db = db;
        _registry = registry;
    }

    [HttpGet("probe-price")]
    public async Task<IActionResult> ProbePrice([FromQuery] int competitorId, [FromQuery] string ean, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ean))
        {
            return BadRequest("EAN es requerido.");
        }

        var normalizedEan = NormalizeEan(ean);
        if (normalizedEan is null)
        {
            return BadRequest("EAN invalido.");
        }

        var product = await _db.Products.AsNoTracking()
            .Select(p => new { p.Id, p.Sku, p.Ean, p.Description })
            .FirstOrDefaultAsync(p => p.Ean == normalizedEan, ct);

        if (product is null)
        {
            // Fallback for any whitespace/non-digit contamination.
            var all = await _db.Products.AsNoTracking()
                .Select(p => new { p.Id, p.Sku, p.Ean, p.Description })
                .ToListAsync(ct);

            product = all.FirstOrDefault(p => NormalizeEan(p.Ean) == normalizedEan);
            if (product is null)
            {
                return NotFound("Producto no encontrado por EAN.");
            }
        }

        var competitor = await _db.Competitors.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == competitorId, ct);
        if (competitor is null)
        {
            return NotFound("Competidor no encontrado.");
        }

        var mapping = await _db.CompetitorProducts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ProductId == product.Id && x.CompetitorId == competitorId, ct);

        if (mapping is null || string.IsNullOrWhiteSpace(mapping.Url))
        {
            return NotFound("El producto no tiene URL guardada para ese competidor.");
        }

        if (string.IsNullOrWhiteSpace(competitor.AdapterId))
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "Competidor sin AdapterId.");
        }

        var adapter = _registry.Get(competitor.AdapterId);
        if (adapter is null)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, $"Adapter no encontrado: {competitor.AdapterId}.");
        }

        object probeResultObj;
        if (adapter is ICompetitorProductProbe prober)
        {
            probeResultObj = await prober.ProbeAsync(
                new CompetitorProductProbeRequest(competitor.BaseUrl ?? string.Empty, mapping.Url),
                ct
            );
        }
        else
        {
            // Fallback: invoke by reflection to avoid type identity issues across plugin load contexts.
            var method = adapter.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(m =>
                    m.Name == "ProbeAsync" &&
                    m.GetParameters().Length == 2 &&
                    m.GetParameters()[1].ParameterType == typeof(CancellationToken));

            if (method is null)
            {
                return StatusCode(StatusCodes.Status501NotImplemented, "Este adapter no soporta probe por URL.");
            }

            var reqType = method.GetParameters()[0].ParameterType;
            var req = Activator.CreateInstance(reqType, competitor.BaseUrl ?? string.Empty, mapping.Url);
            if (req is null)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, "No se pudo crear el request de probe.");
            }

            var taskObj = method.Invoke(adapter, new[] { req, ct });
            if (taskObj is not Task task)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, "ProbeAsync no retorno un Task.");
            }

            await task.ConfigureAwait(false);
            probeResultObj = task.GetType().GetProperty("Result")?.GetValue(task)
                             ?? new { Url = mapping.Url, ListPrice = (decimal?)null, PromoPrice = (decimal?)null };
        }

        var probe = new
        {
            Url = GetProp<string>(probeResultObj, "Url") ?? mapping.Url,
            ListPrice = GetProp<decimal?>(probeResultObj, "ListPrice"),
            PromoPrice = GetProp<decimal?>(probeResultObj, "PromoPrice"),
            RawListText = GetProp<string?>(probeResultObj, "RawListText"),
            RawPromoText = GetProp<string?>(probeResultObj, "RawPromoText"),
            RawSingleAmountText = GetProp<string?>(probeResultObj, "RawSingleAmountText"),
            DecodedListText = GetProp<string?>(probeResultObj, "DecodedListText"),
            DecodedPromoText = GetProp<string?>(probeResultObj, "DecodedPromoText"),
            DecodedSingleAmountText = GetProp<string?>(probeResultObj, "DecodedSingleAmountText"),
        };

        return Ok(new
        {
            product = new
            {
                product.Id,
                product.Sku,
                Ean = product.Ean,
                product.Description
            },
            competitor = new
            {
                competitor.Id,
                competitor.Name,
                competitor.AdapterId
            },
            mapping = new
            {
                mapping.Url,
                mapping.MatchMethod,
                mapping.MatchScore,
                mapping.LastMatchedAt
            },
            probe
        });
    }

    private static T? GetProp<T>(object obj, string name)
    {
        var prop = obj.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
        if (prop is null)
        {
            return default;
        }

        var value = prop.GetValue(obj);
        if (value is null)
        {
            return default;
        }

        return value is T t ? t : (T?)Convert.ChangeType(value, typeof(T));
    }

    private static string? NormalizeEan(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits.Length == 13)
        {
            return digits;
        }

        if (digits.Length == 12)
        {
            return "0" + digits;
        }

        if (digits.Length == 14)
        {
            return digits.Substring(1);
        }

        return digits.Length >= 8 ? digits : null;
    }
}
