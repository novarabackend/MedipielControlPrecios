using ClosedXML.Excel;
using Medipiel.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Medipiel.Api.Controllers;

[ApiController]
[Route("api/reports")]
public class ReportsController : ControllerBase
{
    private readonly AppDbContext _db;

    public ReportsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("excel")]
    public async Task<IActionResult> ExportExcel(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] int? brandId,
        [FromQuery] int? categoryId,
        [FromQuery] string? productIds,
        [FromQuery] string? format,
        [FromQuery] string? layout)
    {
        var latestDate = await _db.PriceSnapshots
            .AsNoTracking()
            .MaxAsync(x => (DateOnly?)x.SnapshotDate);

        if (latestDate is null)
        {
            return BadRequest("No hay snapshots disponibles.");
        }

        var fromDate = from ?? latestDate.Value;
        var toDate = to ?? fromDate;
        if (fromDate > toDate)
        {
            (fromDate, toDate) = (toDate, fromDate);
        }

        var competitors = await _db.Competitors
            .AsNoTracking()
            .Where(x => x.IsActive)
            .ToListAsync();

        var orderedCompetitors = competitors
            .OrderBy(x => ResolveOrder(x.AdapterId, x.Name))
            .ThenBy(x => x.Name)
            .ToList();

        var baselineCompetitorId = orderedCompetitors
            .FirstOrDefault(x =>
                !string.IsNullOrWhiteSpace(x.AdapterId) &&
                x.AdapterId.Trim().Equals("medipiel", StringComparison.OrdinalIgnoreCase))
            ?.Id;

        var productsQuery =
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
                Product = p,
                Brand = b,
                Supplier = s,
                Category = c,
                Line = l
            };

        var productIdList = ParseIds(productIds);

        if (productIdList.Count > 0)
        {
            productsQuery = productsQuery.Where(x => productIdList.Contains(x.Product.Id));
        }

        if (brandId.HasValue)
        {
            productsQuery = productsQuery.Where(x => x.Product.BrandId == brandId.Value);
        }

        if (categoryId.HasValue)
        {
            productsQuery = productsQuery.Where(x => x.Product.CategoryId == categoryId.Value);
        }

        var products = await productsQuery
            .OrderBy(x => x.Product.Description)
            .Select(x => new ReportProductRow(
                x.Product.Id,
                x.Product.Sku,
                x.Product.Ean,
                x.Product.Description,
                x.Brand != null ? x.Brand.Name : null,
                x.Supplier != null ? x.Supplier.Name : null,
                x.Category != null ? x.Category.Name : null,
                x.Line != null ? x.Line.Name : null,
                x.Product.MedipielPromoPrice,
                x.Product.MedipielListPrice,
                x.Product.BrandId,
                x.Product.CategoryId
            ))
            .ToListAsync();

        var productIdsAll = products.Select(x => x.Id).ToList();
        var competitorIds = orderedCompetitors.Select(x => x.Id).ToList();

        var snapshots = await _db.PriceSnapshots.AsNoTracking()
            .Where(x =>
                productIdsAll.Contains(x.ProductId) &&
                competitorIds.Contains(x.CompetitorId) &&
                x.SnapshotDate >= fromDate &&
                x.SnapshotDate <= toDate)
            .ToListAsync();

        var competitorProducts = await _db.CompetitorProducts.AsNoTracking()
            .Where(x => productIdsAll.Contains(x.ProductId) && competitorIds.Contains(x.CompetitorId))
            .ToListAsync();

        var snapshotMap = snapshots.ToDictionary(
            x => (x.SnapshotDate, x.ProductId, x.CompetitorId),
            x => x
        );

        var competitorProductMap = competitorProducts.ToDictionary(
            x => (x.ProductId, x.CompetitorId),
            x => x
        );

        var dates = new List<DateOnly>();
        for (var d = fromDate; d <= toDate; d = d.AddDays(1))
        {
            dates.Add(d);
        }

        var resolvedFormat = layout ?? format;
        var useLongFormat = string.Equals(resolvedFormat, "long", StringComparison.OrdinalIgnoreCase);
        if (useLongFormat)
        {
            return BuildLongReport(
                products,
                orderedCompetitors,
                dates,
                snapshotMap,
                competitorProductMap,
                baselineCompetitorId,
                fromDate,
                toDate);
        }

        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Reporte");

        var baseHeaders = new[]
        {
            "SKU",
            "EAN",
            "Descripcion",
            "Marca",
            "Proveedor",
            "Categoria",
            "Linea",
            "Precio Descuento",
            "Precio Normal"
        };

        var competitorHeaders = new[]
        {
            "Precio Lista",
            "Precio Promo",
            "Diff Lista $",
            "Diff Lista %",
            "Diff Promo $",
            "Diff Promo %",
            "Match Metodo",
            "Match Score",
            "Nombre",
            "URL"
        };

        var baseColCount = baseHeaders.Length;
        var competitorBlockSize = competitorHeaders.Length;
        var totalColumnsPerDate = orderedCompetitors.Count * competitorBlockSize;

        for (var i = 0; i < baseHeaders.Length; i++)
        {
            var col = i + 1;
            sheet.Cell(1, col).Value = baseHeaders[i];
            sheet.Range(1, col, 3, col).Merge();
        }

        var currentCol = baseColCount + 1;
        foreach (var date in dates)
        {
            var dateStart = currentCol;
            var dateEnd = dateStart + totalColumnsPerDate - 1;
            sheet.Cell(1, dateStart).Value = date.ToString("dd/MM/yy");
            sheet.Range(1, dateStart, 1, dateEnd).Merge();

            foreach (var competitor in orderedCompetitors)
            {
                var compStart = currentCol;
                var compEnd = compStart + competitorBlockSize - 1;
                sheet.Cell(2, compStart).Value = competitor.Name;
                sheet.Range(2, compStart, 2, compEnd).Merge();

                for (var i = 0; i < competitorHeaders.Length; i++)
                {
                    sheet.Cell(3, compStart + i).Value = competitorHeaders[i];
                }

                currentCol = compEnd + 1;
            }
        }

        var headerRange = sheet.Range(1, 1, 3, baseColCount + totalColumnsPerDate * dates.Count);
        headerRange.Style.Font.SetBold();
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#f2f4f8");

        sheet.Row(1).Height = 24;
        sheet.Row(2).Height = 22;
        sheet.Row(3).Height = 22;

        var rowIndex = 4;
        foreach (var product in products)
        {
            sheet.Cell(rowIndex, 1).Value = product.Sku ?? string.Empty;
            sheet.Cell(rowIndex, 2).Value = product.Ean ?? string.Empty;
            sheet.Cell(rowIndex, 3).Value = product.Description;
            sheet.Cell(rowIndex, 4).Value = product.BrandName ?? string.Empty;
            sheet.Cell(rowIndex, 5).Value = product.SupplierName ?? string.Empty;
            sheet.Cell(rowIndex, 6).Value = product.CategoryName ?? string.Empty;
            sheet.Cell(rowIndex, 7).Value = product.LineName ?? string.Empty;
            sheet.Cell(rowIndex, 8).Value = product.MedipielPromoPrice;
            sheet.Cell(rowIndex, 9).Value = product.MedipielListPrice;

            currentCol = baseColCount + 1;
            foreach (var date in dates)
            {
                var baselineList = baselineCompetitorId.HasValue
                    ? snapshotMap.TryGetValue((date, product.Id, baselineCompetitorId.Value), out var baselineSnapshot)
                        ? baselineSnapshot.ListPrice
                        : null
                    : null;

                var baselinePromo = baselineCompetitorId.HasValue
                    ? snapshotMap.TryGetValue((date, product.Id, baselineCompetitorId.Value), out var baselineSnapshotPromo)
                        ? baselineSnapshotPromo.PromoPrice
                        : null
                    : null;

                foreach (var competitor in orderedCompetitors)
                {
                    snapshotMap.TryGetValue((date, product.Id, competitor.Id), out var snapshot);
                    competitorProductMap.TryGetValue((product.Id, competitor.Id), out var cp);

                    var listPrice = snapshot?.ListPrice;
                    var promoPrice = snapshot?.PromoPrice;
                    var diffList = ComputeDiff(listPrice, baselineList);
                    var diffListPercent = ComputeDiffPercent(listPrice, baselineList);
                    var diffPromo = ComputeDiff(promoPrice, baselinePromo);
                    var diffPromoPercent = ComputeDiffPercent(promoPrice, baselinePromo);

                    sheet.Cell(rowIndex, currentCol + 0).Value = listPrice;
                    sheet.Cell(rowIndex, currentCol + 1).Value = promoPrice;
                    sheet.Cell(rowIndex, currentCol + 2).Value = diffList;
                    sheet.Cell(rowIndex, currentCol + 3).Value = diffListPercent;
                    sheet.Cell(rowIndex, currentCol + 4).Value = diffPromo;
                    sheet.Cell(rowIndex, currentCol + 5).Value = diffPromoPercent;
                    sheet.Cell(rowIndex, currentCol + 6).Value = cp?.MatchMethod ?? string.Empty;
                    sheet.Cell(rowIndex, currentCol + 7).Value = cp?.MatchScore;
                    sheet.Cell(rowIndex, currentCol + 8).Value = cp?.Name ?? string.Empty;
                    sheet.Cell(rowIndex, currentCol + 9).Value = cp?.Url ?? string.Empty;

                    currentCol += competitorBlockSize;
                }
            }

            rowIndex += 1;
        }

        var moneyColumns = new List<int> { 8, 9 };
        var percentColumns = new List<int>();

        var startCol = baseColCount + 1;
        for (var dateIndex = 0; dateIndex < dates.Count; dateIndex++)
        {
            for (var competitorIndex = 0; competitorIndex < orderedCompetitors.Count; competitorIndex++)
            {
                var blockStart = startCol + (dateIndex * totalColumnsPerDate) + (competitorIndex * competitorBlockSize);
                moneyColumns.Add(blockStart + 0);
                moneyColumns.Add(blockStart + 1);
                moneyColumns.Add(blockStart + 2);
                moneyColumns.Add(blockStart + 4);
                percentColumns.Add(blockStart + 3);
                percentColumns.Add(blockStart + 5);
            }
        }

        foreach (var col in moneyColumns.Distinct())
        {
            sheet.Column(col).Style.NumberFormat.Format = "#,##0";
        }

        foreach (var col in percentColumns.Distinct())
        {
            sheet.Column(col).Style.NumberFormat.Format = "0.0%";
        }

        sheet.SheetView.FreezeRows(3);
        sheet.SheetView.FreezeColumns(baseColCount);
        sheet.Columns().AdjustToContents();

        await using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        var content = stream.ToArray();

        var fileName = $"reporte_precios_{fromDate:yyyyMMdd}-{toDate:yyyyMMdd}.xlsx";
        return File(
            content,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName
        );
    }

    private IActionResult BuildLongReport(
        List<ReportProductRow> products,
        List<Models.Competitor> competitors,
        List<DateOnly> dates,
        Dictionary<(DateOnly, int, int), Models.PriceSnapshot> snapshotMap,
        Dictionary<(int, int), Models.CompetitorProduct> competitorProductMap,
        int? baselineCompetitorId,
        DateOnly fromDate,
        DateOnly toDate)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Reporte");

        var headers = new List<string>
        {
            "Fecha",
            "SKU",
            "EAN",
            "Descripcion",
            "Marca",
            "Proveedor",
            "Categoria",
            "Linea",
            "Precio-Descuento",
            "Precio-Normal",
            "Competidor",
            "Precio Lista",
            "Precio Promo",
            "Diff Lista $",
            "Diff Lista %",
            "Diff Promo $",
            "Diff Promo %",
            "Match Metodo",
            "Match Score",
            "Nombre",
            "URL"
        };

        for (var i = 0; i < headers.Count; i++)
        {
            sheet.Cell(1, i + 1).Value = headers[i];
        }

        var rowIndex = 2;
        foreach (var product in products)
        {
            foreach (var date in dates)
            {
                var baselineList = baselineCompetitorId.HasValue
                    ? snapshotMap.TryGetValue((date, product.Id, baselineCompetitorId.Value), out var baselineSnapshot)
                        ? baselineSnapshot.ListPrice
                        : null
                    : null;

                var baselinePromo = baselineCompetitorId.HasValue
                    ? snapshotMap.TryGetValue((date, product.Id, baselineCompetitorId.Value), out var baselineSnapshotPromo)
                        ? baselineSnapshotPromo.PromoPrice
                        : null
                    : null;

                foreach (var competitor in competitors)
                {
                    snapshotMap.TryGetValue((date, product.Id, competitor.Id), out var snapshot);
                    competitorProductMap.TryGetValue((product.Id, competitor.Id), out var cp);

                    var listPrice = snapshot?.ListPrice;
                    var promoPrice = snapshot?.PromoPrice;
                    var diffList = ComputeDiff(listPrice, baselineList);
                    var diffListPercent = ComputeDiffPercent(listPrice, baselineList);
                    var diffPromo = ComputeDiff(promoPrice, baselinePromo);
                    var diffPromoPercent = ComputeDiffPercent(promoPrice, baselinePromo);

                    sheet.Cell(rowIndex, 1).Value = date.ToString("dd/MM/yy");
                    sheet.Cell(rowIndex, 2).Value = product.Sku ?? string.Empty;
                    sheet.Cell(rowIndex, 3).Value = product.Ean ?? string.Empty;
                    sheet.Cell(rowIndex, 4).Value = product.Description;
                    sheet.Cell(rowIndex, 5).Value = product.BrandName ?? string.Empty;
                    sheet.Cell(rowIndex, 6).Value = product.SupplierName ?? string.Empty;
                    sheet.Cell(rowIndex, 7).Value = product.CategoryName ?? string.Empty;
                    sheet.Cell(rowIndex, 8).Value = product.LineName ?? string.Empty;
                    sheet.Cell(rowIndex, 9).Value = product.MedipielPromoPrice;
                    sheet.Cell(rowIndex, 10).Value = product.MedipielListPrice;
                    sheet.Cell(rowIndex, 11).Value = competitor.Name;
                    sheet.Cell(rowIndex, 12).Value = listPrice;
                    sheet.Cell(rowIndex, 13).Value = promoPrice;
                    sheet.Cell(rowIndex, 14).Value = diffList;
                    sheet.Cell(rowIndex, 15).Value = diffListPercent;
                    sheet.Cell(rowIndex, 16).Value = diffPromo;
                    sheet.Cell(rowIndex, 17).Value = diffPromoPercent;
                    sheet.Cell(rowIndex, 18).Value = cp?.MatchMethod ?? string.Empty;
                    sheet.Cell(rowIndex, 19).Value = cp?.MatchScore;
                    sheet.Cell(rowIndex, 20).Value = cp?.Name ?? string.Empty;
                    sheet.Cell(rowIndex, 21).Value = cp?.Url ?? string.Empty;

                    rowIndex += 1;
                }
            }
        }

        var headerRange = sheet.Range(1, 1, 1, headers.Count);
        headerRange.Style.Font.SetBold();
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#f2f4f8");

        var moneyColumns = new[] { 9, 10, 12, 13, 14, 16 };
        var percentColumns = new[] { 15, 17 };

        foreach (var col in moneyColumns.Distinct())
        {
            sheet.Column(col).Style.NumberFormat.Format = "#,##0";
        }

        foreach (var col in percentColumns.Distinct())
        {
            sheet.Column(col).Style.NumberFormat.Format = "0.0%";
        }

        sheet.SheetView.FreezeRows(1);
        sheet.SheetView.FreezeColumns(11);
        sheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        var content = stream.ToArray();

        var fileName = $"reporte_precios_{fromDate:yyyyMMdd}-{toDate:yyyyMMdd}.xlsx";
        return File(
            content,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName
        );
    }

    private static List<int> ParseIds(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new List<int>();
        }

        var list = new List<int>();
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(part.Trim(), out var id))
            {
                list.Add(id);
            }
        }

        return list;
    }

    private static decimal? ComputeDiff(decimal? value, decimal? baseValue)
    {
        if (!value.HasValue || !baseValue.HasValue)
        {
            return null;
        }

        return value.Value - baseValue.Value;
    }

    private static decimal? ComputeDiffPercent(decimal? value, decimal? baseValue)
    {
        if (!value.HasValue || !baseValue.HasValue || baseValue.Value == 0)
        {
            return null;
        }

        return (value.Value - baseValue.Value) / baseValue.Value;
    }

    private static int ResolveOrder(string? adapterId, string? name)
    {
        if (!string.IsNullOrWhiteSpace(adapterId) &&
            adapterId.Trim().Equals("medipiel", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return 999;
        }

        var normalized = name.Trim().ToLowerInvariant();
        if (normalized.Contains("bella piel"))
        {
            return 1;
        }

        if (normalized.Contains("linea estetica"))
        {
            return 2;
        }

        if (normalized.Contains("farmatodo"))
        {
            return 3;
        }

        if (normalized.Contains("cruz verde"))
        {
            return 4;
        }

        return 99;
    }
}

public sealed record ReportProductRow(
    int Id,
    string? Sku,
    string? Ean,
    string Description,
    string? BrandName,
    string? SupplierName,
    string? CategoryName,
    string? LineName,
    decimal? MedipielPromoPrice,
    decimal? MedipielListPrice,
    int? BrandId,
    int? CategoryId
);
