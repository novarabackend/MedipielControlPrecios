using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Medipiel.Competitors.Abstractions;
using Medipiel.Competitors.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Medipiel.Competitors.Medipiel;

public sealed class MedipielAdapter : CompetitorAdapterBase
{
    public MedipielAdapter(IConfiguration configuration, ILogger<MedipielAdapter> logger)
        : base(configuration, logger)
    {
    }

    public override string AdapterId => "medipiel";
    public override string Name => "Medipiel";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public override async Task<AdapterRunResult> RunAsync(AdapterContext context, CancellationToken ct)
    {
        var connectionString = GetConnectionString(context.ConnectionName);
        var db = CreateDb(connectionString);
        var delayMs = GetDelayMs("Adapters:Medipiel:DelayMs", 120);
        var pageSize = Math.Clamp(Configuration.GetValue<int?>("Adapters:Medipiel:PageSize") ?? 50, 1, 100);
        var includeOutOfStock = Configuration.GetValue<bool?>("Adapters:Medipiel:IncludeOutOfStock") ?? true;
        var maxPagesPerBrand = Math.Clamp(Configuration.GetValue<int?>("Adapters:Medipiel:MaxPagesPerBrand") ?? 400, 1, 2000);

        var products = await db.LoadProductsAsync(
            context.CompetitorId,
            context.RunDate,
            context.OnlyNew,
            context.BatchSize,
            requireEan: true,
            ct
        );

        var counters = new Counters();
        var total = products.Count;
        var noMatch = 0;
        var logEvery = Math.Max(25, total / 10);

        Logger.LogInformation(
            "Medipiel: inicio {Total} productos (OnlyNew={OnlyNew}, BatchSize={BatchSize}).",
            total,
            context.OnlyNew,
            context.BatchSize
        );

        if (total == 0)
        {
            return new AdapterRunResult(0, 0, 0, 0, null);
        }

        var baseUrl = context.BaseUrl.TrimEnd('/');
        var brandList = await LoadBrandListAsync(baseUrl, delayMs, ct);
        var brandByNormalized = brandList
            .Where(x => !string.IsNullOrWhiteSpace(x.Name) && x.IsActive)
            .GroupBy(x => NormalizeBrandName(x.Name))
            .Where(g => !string.IsNullOrWhiteSpace(g.Key))
            .ToDictionary(g => g.Key!, g => g.First());

        var grouped = products
            .Select(p => new ProductTarget(
                p,
                NormalizeEan(p.Ean),
                NormalizeBrandName(p.BrandName)
            ))
            .GroupBy(x => x.BrandKey ?? string.Empty)
            .ToList();

        var brandCatalogCache = new Dictionary<int, Dictionary<string, CatalogMatch>>();

        foreach (var group in grouped)
        {
            ct.ThrowIfCancellationRequested();

            var groupTargets = group.ToList();
            var brandKey = group.Key;
            var resolvedBrand = ResolveBrand(brandKey, brandByNormalized);

            if (resolvedBrand is null)
            {
                Logger.LogInformation(
                    "Medipiel: marca sin mapeo '{BrandKey}' ({Count} productos).",
                    brandKey,
                    groupTargets.Count
                );

                foreach (var target in groupTargets)
                {
                    counters.Processed += 1;
                    await db.MarkNoMatchAsync(target.Row.Id, context.CompetitorId, ct);
                    noMatch += 1;
                }

                continue;
            }

            if (!brandCatalogCache.TryGetValue(resolvedBrand.Id, out var catalogByEan))
            {
                catalogByEan = await LoadBrandCatalogByEanAsync(
                    db,
                    context,
                    baseUrl,
                    resolvedBrand,
                    delayMs,
                    pageSize,
                    maxPagesPerBrand,
                    includeOutOfStock,
                    ct
                );

                brandCatalogCache[resolvedBrand.Id] = catalogByEan;
            }

            foreach (var target in groupTargets)
            {
                counters.Processed += 1;

                if (string.IsNullOrWhiteSpace(target.Ean) || !catalogByEan.TryGetValue(target.Ean, out var match))
                {
                    await db.MarkNoMatchAsync(target.Row.Id, context.CompetitorId, ct);
                    noMatch += 1;
                    continue;
                }

                await db.UpsertCompetitorProductAsync(
                    target.Row.Id,
                    context.CompetitorId,
                    match.Url,
                    match.Name,
                    "ean",
                    1,
                    DateTime.UtcNow,
                    ct
                );

                await db.UpsertPriceSnapshotAsync(
                    target.Row.Id,
                    context.CompetitorId,
                    context.RunDate.Date,
                    match.ListPrice,
                    match.PromoPrice,
                    ct
                );

                counters.Updated += 1;

                if (counters.Processed % logEvery == 0 || counters.Processed == total)
                {
                    Logger.LogInformation(
                        "Medipiel: progreso {Processed}/{Total} (Updated={Updated}, Errors={Errors}, NoMatch={NoMatch}).",
                        counters.Processed,
                        total,
                        counters.Updated,
                        counters.Errors,
                        noMatch
                    );
                }
            }
        }

        Logger.LogInformation(
            "Medipiel: fin Processed={Processed} Updated={Updated} Errors={Errors} NoMatch={NoMatch}.",
            counters.Processed,
            counters.Updated,
            counters.Errors,
            noMatch
        );

        return new AdapterRunResult(
            counters.Processed,
            counters.Created,
            counters.Updated,
            counters.Errors,
            null
        );
    }

    private async Task<List<BrandItem>> LoadBrandListAsync(
        string baseUrl,
        int delayMs,
        CancellationToken ct)
    {
        var url = Combine(baseUrl, "/api/catalog_system/pub/brand/list");
        try
        {
            var json = await GetHtmlAsync(url, delayMs, ct);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<BrandItem>();
            }

            var items = JsonSerializer.Deserialize<List<BrandItem>>(json, JsonOptions);
            return items ?? new List<BrandItem>();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Medipiel: error cargando lista de marcas.");
            return new List<BrandItem>();
        }
    }

    private async Task<Dictionary<string, CatalogMatch>> LoadBrandCatalogByEanAsync(
        CompetitorDb db,
        AdapterContext context,
        string baseUrl,
        BrandItem brand,
        int delayMs,
        int pageSize,
        int maxPagesPerBrand,
        bool includeOutOfStock,
        CancellationToken ct)
    {
        var results = new Dictionary<string, CatalogMatch>(StringComparer.Ordinal);
        var from = 0;
        var page = 0;
        int? total = null;

        while (page < maxPagesPerBrand)
        {
            ct.ThrowIfCancellationRequested();

            var to = from + pageSize - 1;
            var requestUrl = Combine(
                baseUrl,
                $"/api/catalog_system/pub/products/search?fq=brandId:{brand.Id}&_from={from}&_to={to}"
            );

            using var response = await HttpClient.GetAsync(requestUrl, delayMs, ct);
            if (!response.IsSuccessStatusCode)
            {
                Logger.LogWarning(
                    "Medipiel: HTTP {Status} consultando marca {Brand} (id={BrandId}) from={From} to={To}.",
                    (int)response.StatusCode,
                    brand.Name,
                    brand.Id,
                    from,
                    to
                );
                break;
            }

            total ??= ParseTotalResources(response);

            var json = await response.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(json))
            {
                break;
            }

            var products = JsonSerializer.Deserialize<List<CatalogProduct>>(json, JsonOptions);
            if (products is null || products.Count == 0)
            {
                break;
            }

            foreach (var product in products)
            {
                if (product.Items is null || product.Items.Count == 0)
                {
                    continue;
                }

                foreach (var sku in product.Items)
                {
                    var ean = NormalizeEan(sku.Ean);
                    if (string.IsNullOrWhiteSpace(ean))
                    {
                        continue;
                    }

                    var offer = ResolveOffer(sku.Sellers, includeOutOfStock);
                    if (offer is null)
                    {
                        continue;
                    }

                    var prices = ResolvePrices(offer);
                    var url = NormalizeProductUrl(baseUrl, product.Link);
                    var match = new CatalogMatch(
                        url,
                        product.ProductName,
                        prices.ListPrice,
                        prices.PromoPrice
                    );

                    results[ean] = match;

                    await db.UpsertCompetitorCatalogAsync(
                        context.CompetitorId,
                        url,
                        product.ProductName,
                        product.ProductName,
                        ean,
                        sku.ItemId,
                        product.Brand,
                        string.Join(" | ", product.Categories ?? []),
                        prices.ListPrice,
                        prices.PromoPrice,
                        DateTime.UtcNow,
                        ct
                    );
                }
            }

            from += pageSize;
            page += 1;

            if (total.HasValue && from >= total.Value)
            {
                break;
            }
        }

        Logger.LogInformation(
            "Medipiel: marca {Brand} (id={BrandId}) indexada con {Count} EAN.",
            brand.Name,
            brand.Id,
            results.Count
        );

        return results;
    }

    private static string NormalizeProductUrl(string baseUrl, string? link)
    {
        if (string.IsNullOrWhiteSpace(link))
        {
            return baseUrl;
        }

        return NormalizeUrl(baseUrl, link);
    }

    private static Offer? ResolveOffer(List<Seller>? sellers, bool includeOutOfStock)
    {
        if (sellers is null || sellers.Count == 0)
        {
            return null;
        }

        foreach (var seller in sellers)
        {
            if (seller.CommertialOffer is null)
            {
                continue;
            }

            if (includeOutOfStock)
            {
                return seller.CommertialOffer;
            }

            if (seller.CommertialOffer.IsAvailable == true)
            {
                return seller.CommertialOffer;
            }

            if (seller.CommertialOffer.AvailableQuantity.GetValueOrDefault() > 0)
            {
                return seller.CommertialOffer;
            }
        }

        return null;
    }

    private static PriceValues ResolvePrices(Offer offer)
    {
        var list = offer.ListPrice;
        var promo = offer.Price;

        if (promo is null || promo <= 0)
        {
            promo = list;
        }

        if (list is null || list <= 0)
        {
            list = promo;
        }

        return new PriceValues(list, promo);
    }

    private static int? ParseTotalResources(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("resources", out var values))
        {
            return null;
        }

        var raw = values.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var slash = raw.LastIndexOf('/');
        if (slash < 0 || slash + 1 >= raw.Length)
        {
            return null;
        }

        return int.TryParse(raw[(slash + 1)..], out var total) ? total : null;
    }

    private static BrandItem? ResolveBrand(string localBrand, Dictionary<string, BrandItem> byNormalized)
    {
        if (string.IsNullOrWhiteSpace(localBrand) || byNormalized.Count == 0)
        {
            return null;
        }

        if (byNormalized.TryGetValue(localBrand, out var exact))
        {
            return exact;
        }

        BrandItem? best = null;
        var bestScore = int.MinValue;

        foreach (var (key, value) in byNormalized)
        {
            var score = 0;
            if (key.Contains(localBrand, StringComparison.Ordinal))
            {
                score = 90;
            }
            else if (localBrand.Contains(key, StringComparison.Ordinal))
            {
                score = 80;
            }
            else
            {
                continue;
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = value;
            }
        }

        return best;
    }

    private static string NormalizeBrandName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var value = raw.Trim();
        var split = value.IndexOf(" - ", StringComparison.Ordinal);
        if (split >= 0 && split + 3 < value.Length)
        {
            value = value[(split + 3)..];
        }

        var normalized = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToUpperInvariant(ch));
            }
        }

        return sb.ToString();
    }

    private static string? NormalizeEan(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length == 14 && digits.StartsWith('0'))
        {
            digits = digits[1..];
        }

        return digits.Length >= 12 ? digits : null;
    }

    private sealed record ProductTarget(ProductRow Row, string? Ean, string BrandKey);
    private sealed record CatalogMatch(string Url, string? Name, decimal? ListPrice, decimal? PromoPrice);
    private sealed record PriceValues(decimal? ListPrice, decimal? PromoPrice);

    private sealed class Counters
    {
        public int Processed { get; set; }
        public int Created { get; set; }
        public int Updated { get; set; }
        public int Errors { get; set; }
    }

    private sealed record BrandItem(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("isActive")] bool IsActive
    );

    private sealed record CatalogProduct(
        [property: JsonPropertyName("productId")] string? ProductId,
        [property: JsonPropertyName("productName")] string? ProductName,
        [property: JsonPropertyName("brand")] string? Brand,
        [property: JsonPropertyName("link")] string? Link,
        [property: JsonPropertyName("categories")] List<string>? Categories,
        [property: JsonPropertyName("items")] List<CatalogSku>? Items
    );

    private sealed record CatalogSku(
        [property: JsonPropertyName("itemId")] string? ItemId,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("ean")] string? Ean,
        [property: JsonPropertyName("sellers")] List<Seller>? Sellers
    );

    private sealed record Seller(
        [property: JsonPropertyName("sellerId")] string? SellerId,
        [property: JsonPropertyName("sellerName")] string? SellerName,
        [property: JsonPropertyName("commertialOffer")] Offer? CommertialOffer
    );

    private sealed record Offer(
        [property: JsonPropertyName("Price")] decimal? Price,
        [property: JsonPropertyName("ListPrice")] decimal? ListPrice,
        [property: JsonPropertyName("AvailableQuantity")] int? AvailableQuantity,
        [property: JsonPropertyName("IsAvailable")] bool? IsAvailable
    );
}
