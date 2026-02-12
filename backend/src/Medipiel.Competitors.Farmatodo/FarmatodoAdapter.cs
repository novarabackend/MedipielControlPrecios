using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Medipiel.Competitors.Abstractions;
using Medipiel.Competitors.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Medipiel.Competitors.Farmatodo;

public sealed class FarmatodoAdapter : CompetitorAdapterBase
{
    public FarmatodoAdapter(IConfiguration configuration, ILogger<FarmatodoAdapter> logger)
        : base(configuration, logger)
    {
    }

    public override string AdapterId => "farmatodo";
    public override string Name => "Farmatodo";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public override async Task<AdapterRunResult> RunAsync(AdapterContext context, CancellationToken ct)
    {
        var connectionString = GetConnectionString(context.ConnectionName);
        var db = CreateDb(connectionString);
        var delayMs = GetDelayMs("Adapters:Farmatodo:DelayMs", 150);

        var algoliaEndpoint =
            Configuration.GetValue<string>("Adapters:Farmatodo:Algolia:Endpoint")
            ?? "https://api-search.farmatodo.com/1/indexes/*/queries?x-algolia-agent=Algolia%20for%20JavaScript%20(4.5.1)%3B%20Browser";
        var algoliaAppId =
            Configuration.GetValue<string>("Adapters:Farmatodo:Algolia:ApplicationId") ?? "VCOJEYD2PO";
        var algoliaApiKey =
            Configuration.GetValue<string>("Adapters:Farmatodo:Algolia:ApiKey") ?? "eb9544fe7bfe7ec4c1aa5e5bf7740feb";
        var algoliaIndexName =
            Configuration.GetValue<string>("Adapters:Farmatodo:Algolia:IndexName") ?? "products-colombia";
        var algoliaCityHeader =
            Configuration.GetValue<string>("Adapters:Farmatodo:Algolia:CityHeader") ?? "BOG";
        var hitsPerPage =
            Math.Clamp(Configuration.GetValue<int?>("Adapters:Farmatodo:Algolia:HitsPerPage") ?? 500, 1, 1000);
        var storeWithStock =
            Configuration.GetValue<int?>("Adapters:Farmatodo:Algolia:StoreWithStock") ?? 26;
        var filters =
            Configuration.GetValue<string>("Adapters:Farmatodo:Algolia:Filters")
            ?? "outofstore:false AND NOT rms_class:SAMPLING";

        var products = await db.LoadProductsAsync(
            context.CompetitorId,
            context.RunDate,
            context.OnlyNew,
            context.BatchSize,
            requireEan: true,
            ct
        );

        var counters = new Counters();
        var noMatchCount = 0;
        var total = products.Count;
        var logEvery = Math.Max(25, total / 10);

        // Group by BrandName to avoid 1-request-per-product. We query Algolia once per brand and match by EAN.
        var groups = products
            .GroupBy(p => NormalizeBrandQuery(p.BrandName))
            .ToDictionary(g => g.Key ?? string.Empty, g => g.ToList());

        using var http = CreateAlgoliaHttpClient(algoliaAppId, algoliaApiKey, algoliaCityHeader);

        Logger.LogInformation(
            "Farmatodo: inicio {Total} productos ({Brands} marcas, OnlyNew={OnlyNew}, BatchSize={BatchSize}).",
            total,
            groups.Keys.Count,
            context.OnlyNew,
            context.BatchSize
        );

        foreach (var (brandKey, brandProducts) in groups)
        {
            ct.ThrowIfCancellationRequested();

            // Products without a usable brand can still be attempted, but will likely match poorly.
            // We still query using their description fallback.
            var query = string.IsNullOrWhiteSpace(brandKey)
                ? null
                : brandKey;

            if (string.IsNullOrWhiteSpace(query))
            {
                // Fallback: use the first product description token as query seed (keeps request count bounded).
                query = NormalizeSearchSeed(brandProducts.FirstOrDefault()?.Description);
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                // We cannot query anything meaningful.
                foreach (var p in brandProducts)
                {
                    counters.Processed += 1;
                    await db.MarkNoMatchAsync(p.Id, context.CompetitorId, ct);
                    noMatchCount += 1;
                }

                continue;
            }

            // Create map EAN -> products with that EAN (EAN should be unique, but we handle duplicates defensively).
            var productsByEan = brandProducts
                .Where(p => !string.IsNullOrWhiteSpace(p.Ean))
                .GroupBy(p => NormalizeEan(p.Ean))
                .Where(g => !string.IsNullOrWhiteSpace(g.Key))
                .ToDictionary(g => g.Key!, g => g.ToList());

            var remaining = new HashSet<string>(productsByEan.Keys, StringComparer.Ordinal);

            // Step 1: resolve best "marca" facet for this query. This gives us full catalog coverage for that brand.
            var marcaFacet = await ResolveMarcaFacetAsync(
                http,
                algoliaEndpoint,
                algoliaIndexName,
                query,
                filters,
                storeWithStock,
                delayMs,
                ct
            );

            var page = 0;
            var nbPages = 1;
            while (page < nbPages)
            {
                ct.ThrowIfCancellationRequested();

                var pageResult = await SearchBrandPageAsync(
                    http,
                    algoliaEndpoint,
                    algoliaIndexName,
                    query,
                    marcaFacet,
                    hitsPerPage,
                    page,
                    filters,
                    storeWithStock,
                    delayMs,
                    ct
                );

                if (pageResult is null)
                {
                    // If the brand query fails, mark everything in this group as no_match so we can retry later.
                    foreach (var p in brandProducts)
                    {
                        counters.Processed += 1;
                        await db.MarkNoMatchAsync(p.Id, context.CompetitorId, ct);
                        noMatchCount += 1;
                    }

                    break;
                }

                nbPages = Math.Max(1, pageResult.NbPages);

                foreach (var hit in pageResult.Hits)
                {
                    ct.ThrowIfCancellationRequested();

                    var hitEan = NormalizeHitEan(hit);
                    if (hitEan is null || !remaining.Contains(hitEan))
                    {
                        continue;
                    }

                    if (!productsByEan.TryGetValue(hitEan, out var matchingProducts) || matchingProducts.Count == 0)
                    {
                        continue;
                    }

                    var url = BuildProductUrl(context.BaseUrl, hit.Id);
                    if (string.IsNullOrWhiteSpace(url))
                    {
                        continue;
                    }

                    var prices = ResolvePrices(hit);
                    var name = hit.MediaDescription ?? hit.Marca;

                    foreach (var product in matchingProducts)
                    {
                        counters.Processed += 1;

                        await db.UpsertCompetitorProductAsync(
                            product.Id,
                            context.CompetitorId,
                            url,
                            name,
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

                        counters.Updated += 1;

                        if (counters.Processed % logEvery == 0 || counters.Processed == total)
                        {
                            Logger.LogInformation(
                                "Farmatodo: progreso {Processed}/{Total} (Updated={Updated}, Errors={Errors}).",
                                counters.Processed,
                                total,
                                counters.Updated,
                                counters.Errors
                            );
                        }
                    }

                    remaining.Remove(hitEan);
                    if (remaining.Count == 0)
                    {
                        break;
                    }
                }

                if (remaining.Count == 0)
                {
                    break;
                }

                page += 1;
            }

            // Mark remaining products as no match for this run.
            foreach (var ean in remaining)
            {
                if (!productsByEan.TryGetValue(ean, out var list))
                {
                    continue;
                }

                foreach (var p in list)
                {
                    counters.Processed += 1;
                    await db.MarkNoMatchAsync(p.Id, context.CompetitorId, ct);
                    noMatchCount += 1;
                }
            }
        }

        Logger.LogInformation(
            "Farmatodo: fin Processed={Processed} Updated={Updated} Errors={Errors} NoMatch={NoMatch}.",
            counters.Processed,
            counters.Updated,
            counters.Errors,
            noMatchCount
        );

        return new AdapterRunResult(
            counters.Processed,
            counters.Created,
            counters.Updated,
            counters.Errors,
            null
        );
    }

    private static HttpClient CreateAlgoliaHttpClient(string appId, string apiKey, string city)
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All
        };

        var http = new HttpClient(handler, disposeHandler: true);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        http.DefaultRequestHeaders.TryAddWithoutValidation("x-algolia-application-id", appId);
        http.DefaultRequestHeaders.TryAddWithoutValidation("x-algolia-api-key", apiKey);
        http.DefaultRequestHeaders.TryAddWithoutValidation("x-custom-city", city);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; MedipielControlPrecios/1.0)");
        return http;
    }

    private static string? BuildProductUrl(string baseUrl, string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var normalized = id.Trim();
        return Combine(baseUrl.TrimEnd('/'), $"producto/{normalized}");
    }

    private static PriceValues ResolvePrices(AlgoliaHit item)
    {
        var list = item.FullPrice;
        var promo = item.OfferPrice;

        if (promo is null || promo <= 0)
        {
            promo = list;
        }

        if (list is null)
        {
            list = promo;
        }

        return new PriceValues(list, promo);
    }

    private static string? NormalizeBrandQuery(string? brandName)
    {
        if (string.IsNullOrWhiteSpace(brandName))
        {
            return null;
        }

        // Excel imports often come like "0041 - LA ROCHE". We only keep the human name part.
        var s = brandName.Trim();
        var idx = s.IndexOf(" - ", StringComparison.Ordinal);
        if (idx >= 0 && idx + 3 < s.Length)
        {
            s = s[(idx + 3)..];
        }

        s = s.Trim();
        if (s.Length == 0)
        {
            return null;
        }

        // Algolia search is case-insensitive; we keep it small and consistent.
        return s.ToLowerInvariant();
    }

    private static string? NormalizeSearchSeed(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        // Use the first 2 tokens as a minimal seed.
        var tokens = description
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(2)
            .Select(t => t.ToLowerInvariant())
            .ToArray();

        return tokens.Length == 0 ? null : string.Join(' ', tokens);
    }

    private static string? NormalizeEan(string? ean)
    {
        if (string.IsNullOrWhiteSpace(ean))
        {
            return null;
        }

        var digits = new string(ean.Where(char.IsDigit).ToArray());
        if (digits.Length == 14 && digits.StartsWith('0'))
        {
            digits = digits[1..];
        }

        return digits.Length >= 12 ? digits : null;
    }

    private static string? NormalizeHitEan(AlgoliaHit hit)
    {
        // Try primary barcode then barcodeList.
        foreach (var raw in EnumerateBarcodes(hit))
        {
            var normalized = NormalizeEan(raw);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return normalized;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateBarcodes(AlgoliaHit hit)
    {
        if (!string.IsNullOrWhiteSpace(hit.Barcode))
        {
            yield return hit.Barcode!;
        }

        if (hit.BarcodeList is not null)
        {
            foreach (var b in hit.BarcodeList)
            {
                if (!string.IsNullOrWhiteSpace(b))
                {
                    yield return b;
                }
            }
        }
    }

    private async Task<string?> ResolveMarcaFacetAsync(
        HttpClient http,
        string endpoint,
        string indexName,
        string query,
        string filters,
        int storeWithStock,
        int delayMs,
        CancellationToken ct)
    {
        var parameters = new Dictionary<string, string>
        {
            ["hitsPerPage"] = "0",
            ["page"] = "0",
            ["facets"] = "marca",
        };

        if (!string.IsNullOrWhiteSpace(filters))
        {
            parameters["filters"] = filters;
        }

        if (storeWithStock > 0)
        {
            parameters["optionalFilters"] = JsonSerializer.Serialize(new[] { new[] { $"stores_with_stock:{storeWithStock}" } });
        }

        var @params = BuildParamsString(parameters);
        var result = await AlgoliaQueryAsync(http, endpoint, indexName, query, @params, delayMs, ct);
        var facets = result?.Facets?.Marca;
        if (facets is null || facets.Count == 0)
        {
            return null;
        }

        // Best effort: pick the facet that matches the query best (case/space/punct insensitive), prefer higher count.
        var qn = NormalizeKey(query);
        string? best = null;
        var bestScore = int.MinValue;
        var bestCount = -1;

        foreach (var (facet, count) in facets)
        {
            var fn = NormalizeKey(facet);
            var score = 0;

            if (fn == qn)
            {
                score = 100;
            }
            else if (fn.Contains(qn, StringComparison.Ordinal))
            {
                score = 80;
            }
            else if (qn.Contains(fn, StringComparison.Ordinal))
            {
                score = 60;
            }
            else if (fn.StartsWith(qn, StringComparison.Ordinal))
            {
                score = 50;
            }

            if (score > bestScore || (score == bestScore && count > bestCount))
            {
                best = facet;
                bestScore = score;
                bestCount = count;
            }
        }

        return best;
    }

    private async Task<AlgoliaQueryResult?> SearchBrandPageAsync(
        HttpClient http,
        string endpoint,
        string indexName,
        string query,
        string? marcaFacet,
        int hitsPerPage,
        int page,
        string filters,
        int storeWithStock,
        int delayMs,
        CancellationToken ct)
    {
        var parameters = new Dictionary<string, string>
        {
            ["hitsPerPage"] = hitsPerPage.ToString(),
            ["page"] = page.ToString(),
            ["attributesToRetrieve"] = "id,barcode,barcodeList,fullPrice,offerPrice,mediaDescription,marca,categorie,subCategory",
        };

        if (!string.IsNullOrWhiteSpace(filters))
        {
            parameters["filters"] = filters;
        }

        if (storeWithStock > 0)
        {
            parameters["optionalFilters"] = JsonSerializer.Serialize(new[] { new[] { $"stores_with_stock:{storeWithStock}" } });
        }

        if (!string.IsNullOrWhiteSpace(marcaFacet))
        {
            parameters["facetFilters"] = JsonSerializer.Serialize(new[] { new[] { $"marca:{marcaFacet}" } });
        }

        var @params = BuildParamsString(parameters);
        return await AlgoliaQueryAsync(http, endpoint, indexName, query, @params, delayMs, ct);
    }

    private async Task<AlgoliaQueryResult?> AlgoliaQueryAsync(
        HttpClient http,
        string endpoint,
        string indexName,
        string query,
        string @params,
        int delayMs,
        CancellationToken ct)
    {
        if (delayMs > 0)
        {
            await Task.Delay(delayMs, ct);
        }

        try
        {
            var payload = new AlgoliaMultiQueryRequest(new()
            {
                new AlgoliaQuery(indexName, query, @params)
            });

            var json = JsonSerializer.Serialize(payload, JsonOptions);
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                Logger.LogWarning("Farmatodo: Algolia HTTP {Status} for query '{Query}'", (int)response.StatusCode, query);
                return null;
            }

            var responseJson = await response.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(responseJson))
            {
                return null;
            }

            var parsed = JsonSerializer.Deserialize<AlgoliaMultiQueryResponse>(responseJson, JsonOptions);
            return parsed?.Results?.FirstOrDefault();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Farmatodo: Algolia request failed for query '{Query}'", query);
            return null;
        }
    }

    private static string BuildParamsString(Dictionary<string, string> parameters)
    {
        // Algolia expects a querystring-like value where each param value is URL-encoded.
        return string.Join(
            "&",
            parameters.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}")
        );
    }

    private static string NormalizeKey(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToUpperInvariant(ch));
            }
        }

        return sb.ToString();
    }

    private sealed class Counters
    {
        public int Processed { get; set; }
        public int Created { get; set; }
        public int Updated { get; set; }
        public int Errors { get; set; }
    }

    private sealed record AlgoliaMultiQueryRequest(
        [property: JsonPropertyName("requests")] List<AlgoliaQuery> Requests
    );

    private sealed record AlgoliaQuery(
        [property: JsonPropertyName("indexName")] string IndexName,
        [property: JsonPropertyName("query")] string Query,
        [property: JsonPropertyName("params")] string Params
    );

    private sealed record AlgoliaMultiQueryResponse(
        [property: JsonPropertyName("results")] List<AlgoliaQueryResult> Results
    );

    private sealed record AlgoliaQueryResult(
        [property: JsonPropertyName("hits")] List<AlgoliaHit> Hits,
        [property: JsonPropertyName("nbHits")] int NbHits,
        [property: JsonPropertyName("page")] int Page,
        [property: JsonPropertyName("nbPages")] int NbPages,
        [property: JsonPropertyName("hitsPerPage")] int HitsPerPage,
        [property: JsonPropertyName("facets")] AlgoliaFacets? Facets
    );

    private sealed record AlgoliaFacets(
        [property: JsonPropertyName("marca")] Dictionary<string, int>? Marca
    );

    private sealed record AlgoliaHit(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("barcode")] string? Barcode,
        [property: JsonPropertyName("barcodeList")] string[]? BarcodeList,
        [property: JsonPropertyName("fullPrice")] decimal? FullPrice,
        [property: JsonPropertyName("offerPrice")] decimal? OfferPrice,
        [property: JsonPropertyName("mediaDescription")] string? MediaDescription,
        [property: JsonPropertyName("marca")] string? Marca,
        [property: JsonPropertyName("categorie")] string? Categorie,
        [property: JsonPropertyName("subCategory")] string? SubCategory
    );

    private sealed record PriceValues(decimal? ListPrice, decimal? PromoPrice);
}
