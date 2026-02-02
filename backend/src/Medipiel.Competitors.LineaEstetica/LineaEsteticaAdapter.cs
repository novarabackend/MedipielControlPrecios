using System.Text.Json;
using System.Text.Json.Serialization;
using Medipiel.Competitors.Abstractions;
using Medipiel.Competitors.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Medipiel.Competitors.LineaEstetica;

public sealed class LineaEsteticaAdapter : CompetitorAdapterBase
{
    public LineaEsteticaAdapter(IConfiguration configuration, ILogger<LineaEsteticaAdapter> logger)
        : base(configuration, logger)
    {
    }

    public override string AdapterId => "lineaestetica";
    public override string Name => "Linea Estetica";

    public override async Task<AdapterRunResult> RunAsync(AdapterContext context, CancellationToken ct)
    {
        var connectionString = GetConnectionString(context.ConnectionName);
        var db = CreateDb(connectionString);
        var delayMs = GetDelayMs("Adapters:LineaEstetica:DelayMs", 250);
        var products = await db.LoadProductsAsync(
            context.CompetitorId,
            context.RunDate,
            context.OnlyNew,
            context.BatchSize,
            requireEan: true,
            ct
        );

        var stats = new AdapterRunResult(0, 0, 0, 0, null);
        var counters = new Counters();

        var targets = products
            .Where(p => !string.IsNullOrWhiteSpace(p.Ean))
            .ToDictionary(p => p.Ean!, p => p, StringComparer.OrdinalIgnoreCase);

        if (targets.Count > 0)
        {
            Logger.LogInformation("LineaEstetica: {Count} productos por sincronizar via API.", targets.Count);
            await SyncFromStoreApiAsync(db, context, targets, counters, delayMs, ct);
        }

        stats = new AdapterRunResult(
            counters.Processed,
            counters.Created,
            counters.Updated,
            counters.Errors,
            null
        );

        return stats;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private async Task SyncFromStoreApiAsync(
        CompetitorDb db,
        AdapterContext context,
        Dictionary<string, ProductRow> targets,
        Counters counters,
        int delayMs,
        CancellationToken ct)
    {
        var baseUrl = context.BaseUrl.TrimEnd('/');
        var page = 1;

        while (targets.Count > 0)
        {
            var apiUrl = $"{baseUrl}/wp-json/wc/store/products?per_page=100&page={page}";
            var json = await GetHtmlAsync(apiUrl, delayMs, ct);
            if (string.IsNullOrWhiteSpace(json))
            {
                Logger.LogWarning("LineaEstetica: API sin respuesta en pagina {Page}.", page);
                break;
            }

            List<StoreProduct>? items;
            try
            {
                items = JsonSerializer.Deserialize<List<StoreProduct>>(json, JsonOptions);
            }
            catch (JsonException ex)
            {
                Logger.LogWarning(ex, "LineaEstetica: respuesta invalida en pagina {Page}.", page);
                break;
            }

            if (items is null || items.Count == 0)
            {
                break;
            }

            foreach (var item in items)
            {
                var ean = ExtractEan(item);
                if (ean is null || !targets.TryGetValue(ean, out var product))
                {
                    continue;
                }

                var url = item.Permalink;
                if (string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                var prices = ResolvePrices(item.Prices);
                await db.UpsertCompetitorProductAsync(
                    product.Id,
                    context.CompetitorId,
                    url,
                    "ean",
                    1,
                    DateTime.UtcNow,
                    ct
                );

                await db.UpsertPriceSnapshotAsync(
                    product.Id,
                    context.CompetitorId,
                    context.RunDate.Date,
                    prices.ListPrice,
                    prices.PromoPrice,
                    ct
                );

                counters.Processed += 1;
                counters.Updated += 1;
                targets.Remove(ean);
            }

            page += 1;
        }
    }

    private static string? ExtractEan(StoreProduct item)
    {
        if (item.Attributes is null)
        {
            return null;
        }

        foreach (var attribute in item.Attributes)
        {
            var name = attribute.Name ?? string.Empty;
            var taxonomy = attribute.Taxonomy ?? string.Empty;
            if (!name.Equals("EAN", StringComparison.OrdinalIgnoreCase) &&
                !taxonomy.EndsWith("ean", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var term = attribute.Terms?.FirstOrDefault()?.Name;
            if (!string.IsNullOrWhiteSpace(term))
            {
                return term.Trim();
            }
        }

        return null;
    }

    private static PriceValues ResolvePrices(StorePrice? prices)
    {
        if (prices is null)
        {
            return new PriceValues(null, null);
        }

        var list = ParseMoney(prices.RegularPrice) ?? ParseMoney(prices.Price);
        var promo = ParseMoney(prices.SalePrice) ?? ParseMoney(prices.Price);
        if (list is null)
        {
            list = promo;
        }

        return new PriceValues(list, promo);
    }

    private sealed class Counters
    {
        public int Processed { get; set; }
        public int Created { get; set; }
        public int Updated { get; set; }
        public int Errors { get; set; }
    }

    private sealed record StoreProduct(
        string? Permalink,
        StorePrice? Prices,
        List<StoreAttribute>? Attributes
    );

    private sealed record StoreAttribute(
        string? Name,
        string? Taxonomy,
        List<StoreTerm>? Terms
    );

    private sealed record StoreTerm(string? Name);

    private sealed record StorePrice(
        [property: JsonPropertyName("regular_price")] string? RegularPrice,
        [property: JsonPropertyName("sale_price")] string? SalePrice,
        [property: JsonPropertyName("price")] string? Price
    );

    private sealed record PriceValues(decimal? ListPrice, decimal? PromoPrice);
}
