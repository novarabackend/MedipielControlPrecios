using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Medipiel.Competitors.Abstractions;
using Medipiel.Competitors.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Medipiel.Competitors.CruzVerde;

public sealed class CruzVerdeAdapter : CompetitorAdapterBase
{
    private static readonly object AiRateLock = new();
    private static DateTime AiWindowStartUtc = DateTime.MinValue;
    private static int AiWindowCount;
    public CruzVerdeAdapter(IConfiguration configuration, ILogger<CruzVerdeAdapter> logger)
        : base(configuration, logger)
    {
    }

    public override string AdapterId => "cruzverde";
    public override string Name => "Cruz Verde";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly Regex ProductIdRegex = new(@"COCV_\d+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private bool _sessionReady;

    public override async Task<AdapterRunResult> RunAsync(AdapterContext context, CancellationToken ct)
    {
        var connectionString = GetConnectionString(context.ConnectionName);
        var db = CreateDb(connectionString);
        var delayMs = GetDelayMs("Adapters:CruzVerde:DelayMs", 400);
        var apiBase = Configuration.GetValue<string>("Adapters:CruzVerde:ApiBase")
            ?? "https://api.cruzverde.com.co/product-service";
        var loginUrl = Configuration.GetValue<string>("Adapters:CruzVerde:LoginUrl")
            ?? "https://api.cruzverde.com.co/customer-service/login";
        var inventoryId = Configuration.GetValue<string>("Adapters:CruzVerde:InventoryId")
            ?? "COCV_zona120";
        var inventoryZone = Configuration.GetValue<string>("Adapters:CruzVerde:InventoryZone")
            ?? inventoryId;
        var searchLimit = Configuration.GetValue<int?>("Adapters:CruzVerde:SearchLimit") ?? 12;
        var minScore = Configuration.GetValue<double?>("Adapters:CruzVerde:MinScore") ?? 0.55;
        var useAi = Configuration.GetValue<bool?>("Adapters:CruzVerde:UseAi") ?? true;
        var aiMinConfidence = Configuration.GetValue<double?>("Adapters:CruzVerde:AiMinConfidence") ?? 0.6;
        var aiCandidates = Configuration.GetValue<int?>("Adapters:CruzVerde:AiCandidates") ?? 5;
        var catalogRefreshDays = Configuration.GetValue<int?>("Adapters:CruzVerde:CatalogRefreshDays") ?? 7;

        var catalog = await db.LoadCompetitorCatalogAsync(context.CompetitorId, ct);
        if (catalog.Count == 0)
        {
            Logger.LogInformation("CruzVerde: catalogo vacio, construyendo por marcas.");
        }
        else
        {
            var lastExtractedAt = catalog
                .Select(x => x.ExtractedAt ?? DateTime.MinValue)
                .DefaultIfEmpty(DateTime.MinValue)
                .Max();

            if (catalogRefreshDays > 0 && DateTime.UtcNow - lastExtractedAt > TimeSpan.FromDays(catalogRefreshDays))
            {
                Logger.LogInformation(
                    "CruzVerde: catalogo stale (LastExtractedAt={Last}), refrescando por marcas.",
                    lastExtractedAt
                );
                catalog.Clear();
            }
        }

        if (catalog.Count == 0)
        {
            var brandNames = await db.LoadBrandNamesAsync(ct);
            Logger.LogInformation("CruzVerde: marcas base (Medipiel)={Count}.", brandNames.Count);
            await CrawlCatalogByBrandsAsync(
                db,
                context,
                apiBase,
                loginUrl,
                inventoryId,
                inventoryZone,
                searchLimit,
                delayMs,
                brandNames,
                ct);
            catalog = await db.LoadCompetitorCatalogAsync(context.CompetitorId, ct);
            Logger.LogInformation("CruzVerde: catalogo cargado items={Count}.", catalog.Count);
        }
        else
        {
            Logger.LogInformation("CruzVerde: reutilizando catalogo cache items={Count}.", catalog.Count);
        }

        var catalogByBrand = BuildBrandIndex(catalog);
        var catalogByEan = BuildUniqueEanIndex(catalog);

        var products = await db.LoadProductsAsync(
            context.CompetitorId,
            context.RunDate,
            context.OnlyNew,
            context.BatchSize,
            requireEan: false,
            ct
        );

        var counters = new Counters();
        var noMatchCount = 0;
        var total = products.Count;
        var logEvery = Math.Max(25, total / 10);

        Logger.LogInformation(
            "CruzVerde: inicio {Total} productos (OnlyNew={OnlyNew}, BatchSize={BatchSize}).",
            total,
            context.OnlyNew,
            context.BatchSize
        );

        foreach (var product in products)
        {
            ct.ThrowIfCancellationRequested();
            counters.Processed += 1;

            try
            {
                if (!string.IsNullOrWhiteSpace(product.Url))
                {
                    if (await TrySyncByUrlAsync(db, context, product, apiBase, loginUrl, inventoryId, delayMs, ct))
                    {
                        counters.Updated += 1;
                    }
                    else
                    {
                        counters.Errors += 1;
                    }

                    continue;
                }

                var outcome = await TrySyncBySearchAsync(
                    db,
                    context,
                    product,
                    apiBase,
                    loginUrl,
                    inventoryId,
                    inventoryZone,
                    searchLimit,
                    delayMs,
                    minScore,
                    useAi,
                    aiMinConfidence,
                    aiCandidates,
                    catalog,
                    catalogByBrand,
                    catalogByEan,
                    ct);
                if (outcome == MatchOutcome.Matched)
                {
                    counters.Updated += 1;
                }
                else if (outcome == MatchOutcome.NoMatch)
                {
                    await db.MarkNoMatchAsync(product.Id, context.CompetitorId, ct);
                    noMatchCount += 1;
                }
                else
                {
                    counters.Errors += 1;
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "CruzVerde: error procesando producto {ProductId}", product.Id);
                counters.Errors += 1;
            }

            if (counters.Processed % logEvery == 0 || counters.Processed == total)
            {
                Logger.LogInformation(
                    "CruzVerde: progreso {Processed}/{Total} (Updated={Updated}, Errors={Errors}, NoMatch={NoMatch}).",
                    counters.Processed,
                    total,
                    counters.Updated,
                    counters.Errors,
                    noMatchCount
                );
            }
        }

        Logger.LogInformation(
            "CruzVerde: fin Processed={Processed} Updated={Updated} Errors={Errors} NoMatch={NoMatch}.",
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

    private async Task<bool> TrySyncByUrlAsync(
        CompetitorDb db,
        AdapterContext context,
        ProductRow product,
        string apiBase,
        string loginUrl,
        string inventoryId,
        int delayMs,
        CancellationToken ct)
    {
        var productId = ExtractProductId(product.Url!);
        if (string.IsNullOrWhiteSpace(productId))
        {
            return false;
        }

        var item = await GetProductSummaryAsync(apiBase, loginUrl, inventoryId, productId, delayMs, ct);
        if (item is null)
        {
            return false;
        }

        var url = BuildProductUrl(context.BaseUrl, productId, item.PageUrl) ?? product.Url!;
        var prices = ResolvePrices(item.Prices);

        await db.UpsertCompetitorProductAsync(
            product.Id,
            context.CompetitorId,
            url,
            item.Name,
            null,
            null,
            DateTime.UtcNow,
            ct
        );

        await db.UpsertCompetitorCatalogAsync(
            context.CompetitorId,
            url,
            item.Name,
            null,
            null,
            productId,
            item.Brand,
            null,
            prices.ListPrice,
            prices.PromoPrice,
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

        return true;
    }

    private async Task<MatchOutcome> TrySyncBySearchAsync(
        CompetitorDb db,
        AdapterContext context,
        ProductRow product,
        string apiBase,
        string loginUrl,
        string inventoryId,
        string inventoryZone,
        int searchLimit,
        int delayMs,
        double minScore,
        bool useAi,
        double aiMinConfidence,
        int aiCandidates,
        List<CompetitorDb.CompetitorCatalogRow> catalog,
        Dictionary<string, List<CompetitorDb.CompetitorCatalogRow>> catalogByBrand,
        Dictionary<string, CompetitorDb.CompetitorCatalogRow> catalogByEan,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(product.Ean))
        {
            var eanKey = NormalizeEan(product.Ean);
            if (!string.IsNullOrWhiteSpace(eanKey) && catalogByEan.TryGetValue(eanKey!, out var cached))
            {
                return await PersistCatalogMatchAsync(
                        db,
                        context,
                        product,
                        cached,
                        apiBase,
                        loginUrl,
                        inventoryId,
                        delayMs,
                        "ean",
                        1,
                        ct)
                    ? MatchOutcome.Matched
                    : MatchOutcome.Error;
            }

            var eanResult = await SearchAsync(apiBase, loginUrl, inventoryId, inventoryZone, product.Ean!, searchLimit, delayMs, ct);
            if (!eanResult.Success)
            {
                return MatchOutcome.Error;
            }

            var eanHits = eanResult.Hits;
            if (eanHits.Count == 1)
            {
                return await PersistMatchAsync(db, context, product, eanHits[0], "ean", 1, ct)
                    ? MatchOutcome.Matched
                    : MatchOutcome.Error;
            }

            if (eanHits.Count > 1)
            {
                var selection = await SelectCandidateAsync(product.Description, eanHits, "ean", minScore, useAi, aiMinConfidence, aiCandidates, ct);
                if (selection is not null)
                {
                    return await PersistMatchAsync(db, context, product, selection.Product, selection.Method, selection.Score, ct)
                        ? MatchOutcome.Matched
                        : MatchOutcome.Error;
                }
            }
        }

        if (catalog.Count == 0)
        {
            var query = BuildSearchQuery(product.Description);
            if (string.IsNullOrWhiteSpace(query))
            {
                return MatchOutcome.NoMatch;
            }

            var result = await SearchAsync(apiBase, loginUrl, inventoryId, inventoryZone, query, searchLimit, delayMs, ct);
            if (!result.Success)
            {
                return MatchOutcome.Error;
            }

            var hits = result.Hits;
            if (hits.Count == 0)
            {
                return MatchOutcome.NoMatch;
            }

            var selected = await SelectCandidateAsync(product.Description, hits, "name", minScore, useAi, aiMinConfidence, aiCandidates, ct);
            if (selected is null)
            {
                return MatchOutcome.NoMatch;
            }

            return await PersistMatchAsync(db, context, product, selected.Product, selected.Method, selected.Score, ct)
                ? MatchOutcome.Matched
                : MatchOutcome.Error;
        }

        var candidates = ResolveCatalogCandidates(product.BrandName, catalog, catalogByBrand);
        if (candidates.Count == 0)
        {
            return MatchOutcome.NoMatch;
        }

        var selectedFromCatalog = await SelectCatalogCandidateAsync(
            product.Description,
            candidates,
            minScore,
            useAi,
            aiMinConfidence,
            aiCandidates,
            ct);
        if (selectedFromCatalog is null)
        {
            return MatchOutcome.NoMatch;
        }

        return await PersistCatalogMatchAsync(
                db,
                context,
                product,
                selectedFromCatalog.Product,
                apiBase,
                loginUrl,
                inventoryId,
                delayMs,
                selectedFromCatalog.Method,
                selectedFromCatalog.Score,
                ct)
            ? MatchOutcome.Matched
            : MatchOutcome.Error;
    }

    private async Task<bool> PersistMatchAsync(
        CompetitorDb db,
        AdapterContext context,
        ProductRow product,
        SearchHit hit,
        string matchMethod,
        double matchScore,
        CancellationToken ct)
    {
        var url = BuildProductUrl(context.BaseUrl, hit.ProductId, hit.PageUrl);
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        var prices = ResolvePrices(hit.Prices);
        await db.UpsertCompetitorProductAsync(
            product.Id,
            context.CompetitorId,
            url,
            hit.ProductName,
            matchMethod,
            (decimal)matchScore,
            DateTime.UtcNow,
            ct
        );

        await db.UpsertCompetitorCatalogAsync(
            context.CompetitorId,
            url,
            hit.ProductName,
            null,
            matchMethod == "ean" ? NormalizeEan(product.Ean) : null,
            hit.ProductId,
            hit.Brand,
            null,
            prices.ListPrice,
            prices.PromoPrice,
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

        return true;
    }

    private async Task<SearchResult> SearchAsync(
        string apiBase,
        string loginUrl,
        string inventoryId,
        string inventoryZone,
        string query,
        int limit,
        int delayMs,
        CancellationToken ct)
    {
        var paged = await SearchPagedAsync(
            apiBase,
            loginUrl,
            inventoryId,
            inventoryZone,
            query,
            limit,
            offset: 0,
            delayMs,
            ct);

        return new SearchResult(paged.Success, paged.Hits);
    }

    private async Task<SearchPagedResult> SearchPagedAsync(
        string apiBase,
        string loginUrl,
        string inventoryId,
        string inventoryZone,
        string query,
        int limit,
        int offset,
        int delayMs,
        CancellationToken ct)
    {
        var url = BuildSearchUrl(apiBase, query, limit, offset, inventoryId, inventoryZone);
        var json = await GetJsonWithSessionAsync(url, loginUrl, delayMs, ct);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new SearchPagedResult(false, null, new List<SearchHit>());
        }

        try
        {
            var response = JsonSerializer.Deserialize<SearchResponse>(json, JsonOptions);
            return new SearchPagedResult(true, response?.Total, response?.Hits ?? new List<SearchHit>());
        }
        catch (JsonException)
        {
            return new SearchPagedResult(false, null, new List<SearchHit>());
        }
    }

    private async Task<ProductSummaryItem?> GetProductSummaryAsync(
        string apiBase,
        string loginUrl,
        string inventoryId,
        string productId,
        int delayMs,
        CancellationToken ct)
    {
        var url = BuildSummaryUrl(apiBase, productId, inventoryId);
        var json = await GetJsonWithSessionAsync(url, loginUrl, delayMs, ct);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, ProductSummaryItem>>(json, JsonOptions);
            if (map is null)
            {
                return null;
            }

            return map.TryGetValue(productId, out var item) ? item : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<string?> GetJsonWithSessionAsync(string url, string loginUrl, int delayMs, CancellationToken ct)
    {
        await EnsureSessionAsync(loginUrl, delayMs, ct);
        var json = await GetHtmlAsync(url, delayMs, ct);
        if (json is not null)
        {
            return json;
        }

        _sessionReady = false;
        await EnsureSessionAsync(loginUrl, delayMs, ct);
        return await GetHtmlAsync(url, delayMs, ct);
    }

    private async Task EnsureSessionAsync(string loginUrl, int delayMs, CancellationToken ct)
    {
        if (_sessionReady)
        {
            return;
        }

        try
        {
            using var content = new StringContent("{}", Encoding.UTF8, "application/json");
            await HttpClient.PostAsync(loginUrl, content, delayMs, ct);
            _sessionReady = true;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "CruzVerde: login fallido");
        }
    }

    private static string BuildSearchUrl(
        string apiBase,
        string query,
        int limit,
        int offset,
        string inventoryId,
        string inventoryZone)
    {
        var baseUrl = apiBase.TrimEnd('/');
        return $"{baseUrl}/products/search?limit={limit}&offset={offset}&sort=&q={Uri.EscapeDataString(query)}&inventoryId={Uri.EscapeDataString(inventoryId)}&inventoryZone={Uri.EscapeDataString(inventoryZone)}";
    }

    private static string BuildSummaryUrl(string apiBase, string productId, string inventoryId)
    {
        var baseUrl = apiBase.TrimEnd('/');
        return $"{baseUrl}/products/product-summary?ids[]={Uri.EscapeDataString(productId)}&fields=name&fields=prices&fields=brand&fields=pageURL&inventoryId={Uri.EscapeDataString(inventoryId)}";
    }

    private static string? BuildProductUrl(string baseUrl, string? productId, string? pageUrl)
    {
        if (string.IsNullOrWhiteSpace(productId) || string.IsNullOrWhiteSpace(pageUrl))
        {
            return null;
        }

        var slug = pageUrl.Trim('/');
        return Combine(baseUrl.TrimEnd('/'), $"{slug}/{productId}.html");
    }

    private static string? ExtractProductId(string url)
    {
        var match = ProductIdRegex.Match(url);
        return match.Success ? match.Value.ToUpperInvariant() : null;
    }

    private static PriceValues ResolvePrices(PriceMap? prices)
    {
        var list = prices?.ListPrice;
        var promo = prices?.SalePrice;

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

    private async Task<SelectionResult?> SelectCandidateAsync(
        string description,
        List<SearchHit> candidates,
        string defaultMethod,
        double minScore,
        bool useAi,
        double aiMinConfidence,
        int aiCandidates,
        CancellationToken ct)
    {
        var ranked = RankCandidates(description, candidates);
        var best = ranked.FirstOrDefault();
        if (best?.Product is null)
        {
            return null;
        }

        if (best.Score >= minScore)
        {
            return new SelectionResult(best.Product, best.Score, defaultMethod);
        }

        if (!useAi)
        {
            return null;
        }

        var selection = await TrySelectWithAiAsync(description, ranked, aiCandidates, ct);
        if (selection is null || selection.Confidence < aiMinConfidence)
        {
            return null;
        }

        return new SelectionResult(selection.Product, selection.Confidence, "ai");
    }

    private static List<CandidateResult> RankCandidates(string description, List<SearchHit> candidates)
    {
        return candidates
            .Select(candidate =>
            {
                var candidateText = BuildCandidateText(candidate);
                var score = ComputeScore(description, candidateText);
                return new CandidateResult(candidate, score);
            })
            .OrderByDescending(result => result.Score)
            .ToList();
    }

    private static string BuildCandidateText(SearchHit candidate)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(candidate.Brand))
        {
            parts.Add(candidate.Brand);
        }

        if (!string.IsNullOrWhiteSpace(candidate.ProductName))
        {
            parts.Add(candidate.ProductName);
        }

        return string.Join(' ', parts);
    }

    private static double ComputeScore(string source, string candidate)
    {
        var sourceTokens = Tokenize(source);
        var candidateTokens = Tokenize(candidate);

        if (sourceTokens.Count == 0 || candidateTokens.Count == 0)
        {
            return 0;
        }

        var intersection = sourceTokens.Intersect(candidateTokens).Count();
        var union = sourceTokens.Union(candidateTokens).Count();
        if (union == 0)
        {
            return 0;
        }

        var coverage = (double)intersection / sourceTokens.Count;
        var jaccard = (double)intersection / union;
        return (coverage * 0.7) + (jaccard * 0.3);
    }

    private static HashSet<string> Tokenize(string value)
    {
        var normalized = NormalizeText(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new HashSet<string>();
        }

        return normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length > 1)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeText(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();

        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
            else
            {
                builder.Append(' ');
            }
        }

        var cleaned = builder.ToString();
        while (cleaned.Contains("  ", StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace("  ", " ", StringComparison.Ordinal);
        }

        return cleaned.Trim();
    }

    private static string BuildSearchQuery(string description)
    {
        var normalized = NormalizeText(description);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var tokens = normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length > 1)
            .ToList();

        if (tokens.Count == 0)
        {
            return string.Empty;
        }

        if (tokens.Count > 6)
        {
            tokens = tokens.Take(6).ToList();
        }

        return string.Join(' ', tokens);
    }

    private async Task CrawlCatalogByBrandsAsync(
        CompetitorDb db,
        AdapterContext context,
        string apiBase,
        string loginUrl,
        string inventoryId,
        string inventoryZone,
        int searchLimit,
        int delayMs,
        List<string> brandNames,
        CancellationToken ct)
    {
        if (brandNames.Count == 0)
        {
            Logger.LogWarning("CruzVerde: no hay marcas para iniciar crawl.");
            return;
        }

        var visitedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var totalUpserts = 0;

        for (var i = 0; i < brandNames.Count; i += 1)
        {
            ct.ThrowIfCancellationRequested();

            var brand = NormalizeBrandForSearch(brandNames[i]);
            if (string.IsNullOrWhiteSpace(brand))
            {
                continue;
            }

            var offset = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                var page = await SearchPagedAsync(
                    apiBase,
                    loginUrl,
                    inventoryId,
                    inventoryZone,
                    brand,
                    searchLimit,
                    offset,
                    delayMs,
                    ct);

                if (!page.Success)
                {
                    break;
                }

                if (page.Hits.Count == 0)
                {
                    break;
                }

                foreach (var hit in page.Hits)
                {
                    var url = BuildProductUrl(context.BaseUrl, hit.ProductId, hit.PageUrl);
                    if (string.IsNullOrWhiteSpace(url))
                    {
                        continue;
                    }

                    var prices = ResolvePrices(hit.Prices);
                    await db.UpsertCompetitorCatalogAsync(
                        context.CompetitorId,
                        url,
                        hit.ProductName,
                        null,
                        null,
                        hit.ProductId,
                        hit.Brand,
                        null,
                        prices.ListPrice,
                        prices.PromoPrice,
                        DateTime.UtcNow,
                        ct
                    );

                    if (visitedUrls.Add(url))
                    {
                        totalUpserts += 1;
                    }
                }

                offset += Math.Max(1, searchLimit);
                if (page.Total.HasValue && offset >= page.Total.Value)
                {
                    break;
                }

                if (!page.Total.HasValue && page.Hits.Count < searchLimit)
                {
                    break;
                }
            }

            if ((i + 1) % 25 == 0 || i + 1 == brandNames.Count)
            {
                Logger.LogInformation(
                    "CruzVerde: crawl marcas progreso {Done}/{Total} (UrlsUnicas={Unique}).",
                    i + 1,
                    brandNames.Count,
                    visitedUrls.Count
                );
            }
        }

        Logger.LogInformation(
            "CruzVerde: crawl catalogo por marcas finalizado. UrlsUnicas={Unique} Upserts~={Upserts}.",
            visitedUrls.Count,
            totalUpserts
        );
    }

    private static Dictionary<string, List<CompetitorDb.CompetitorCatalogRow>> BuildBrandIndex(
        List<CompetitorDb.CompetitorCatalogRow> catalog)
    {
        var dict = new Dictionary<string, List<CompetitorDb.CompetitorCatalogRow>>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in catalog)
        {
            var key = NormalizeBrandKey(item.Brand);
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (!dict.TryGetValue(key!, out var list))
            {
                list = new List<CompetitorDb.CompetitorCatalogRow>();
                dict[key!] = list;
            }

            list.Add(item);
        }

        return dict;
    }

    private static Dictionary<string, CompetitorDb.CompetitorCatalogRow> BuildUniqueEanIndex(
        List<CompetitorDb.CompetitorCatalogRow> catalog)
    {
        return catalog
            .Select(row => new { Row = row, Ean = NormalizeEan(row.Ean) })
            .Where(x => !string.IsNullOrWhiteSpace(x.Ean))
            .GroupBy(x => x.Ean!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.First().Row, StringComparer.OrdinalIgnoreCase);
    }

    private static List<CompetitorDb.CompetitorCatalogRow> ResolveCatalogCandidates(
        string? brandName,
        List<CompetitorDb.CompetitorCatalogRow> catalog,
        Dictionary<string, List<CompetitorDb.CompetitorCatalogRow>> catalogByBrand)
    {
        var brandKey = NormalizeBrandKey(brandName);
        if (!string.IsNullOrWhiteSpace(brandKey) &&
            catalogByBrand.TryGetValue(brandKey!, out var list) &&
            list.Count > 0)
        {
            return list;
        }

        return catalog;
    }

    private async Task<CatalogSelectionResult?> SelectCatalogCandidateAsync(
        string description,
        List<CompetitorDb.CompetitorCatalogRow> candidates,
        double minScore,
        bool useAi,
        double aiMinConfidence,
        int aiCandidates,
        CancellationToken ct)
    {
        var ranked = RankCatalogCandidates(description, candidates);
        var best = ranked.FirstOrDefault();
        if (best?.Product is null)
        {
            return null;
        }

        if (best.Score >= minScore)
        {
            return new CatalogSelectionResult(best.Product, best.Score, "name");
        }

        if (!useAi)
        {
            return null;
        }

        var selection = await TrySelectCatalogWithAiAsync(description, ranked, aiCandidates, ct);
        if (selection is null || selection.Confidence < aiMinConfidence)
        {
            return null;
        }

        return new CatalogSelectionResult(selection.Product, selection.Confidence, "ai");
    }

    private static List<CatalogCandidateResult> RankCatalogCandidates(
        string description,
        List<CompetitorDb.CompetitorCatalogRow> candidates)
    {
        return candidates
            .Select(candidate =>
            {
                var candidateText = BuildCatalogCandidateText(candidate);
                var score = ComputeScore(description, candidateText);
                return new CatalogCandidateResult(candidate, score);
            })
            .OrderByDescending(result => result.Score)
            .ToList();
    }

    private static string BuildCatalogCandidateText(CompetitorDb.CompetitorCatalogRow product)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(product.Brand))
        {
            parts.Add(product.Brand);
        }

        if (!string.IsNullOrWhiteSpace(product.Name))
        {
            parts.Add(product.Name);
        }

        if (!string.IsNullOrWhiteSpace(product.Description))
        {
            parts.Add(product.Description);
        }

        return string.Join(' ', parts);
    }

    private async Task<CatalogAiSelection?> TrySelectCatalogWithAiAsync(
        string description,
        List<CatalogCandidateResult> ranked,
        int maxCandidates,
        CancellationToken ct)
    {
        var apiKey = ResolveOpenAiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Logger.LogWarning("CruzVerde: OpenAI API key no configurada.");
            return null;
        }

        var maxRequestsPerMinute = Configuration.GetValue<int?>("Adapters:CruzVerde:AiMaxRequestsPerMinute") ?? 10;
        var maxRetries = Configuration.GetValue<int?>("Adapters:CruzVerde:AiMaxRetries") ?? 3;
        var retryBaseDelayMs = Configuration.GetValue<int?>("Adapters:CruzVerde:AiRetryBaseDelayMs") ?? 5000;

        var candidates = ranked
            .Where(r => r.Product is not null)
            .Take(Math.Max(1, maxCandidates))
            .ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        var model = Configuration.GetValue<string>("Adapters:CruzVerde:AiModel") ?? "gpt-4o-mini";
        var payload = new
        {
            model,
            temperature = 0,
            messages = new[]
            {
                new
                {
                    role = "system",
                    content = "Eres un asistente que empareja productos. Elige el mejor candidato. Responde SOLO JSON: {\"index\":number,\"confidence\":number,\"reason\":string}. Si ninguno coincide, usa index -1 y confidence 0."
                },
                new
                {
                    role = "user",
                    content = BuildCatalogAiPrompt(description, candidates)
                }
            }
        };

        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        for (var attempt = 0; attempt <= maxRetries; attempt += 1)
        {
            await EnforceAiRateLimitAsync(maxRequestsPerMinute, ct);

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var response = await http.PostAsync("https://api.openai.com/v1/chat/completions", content, ct);
            if (response.IsSuccessStatusCode)
            {
                var responseJson = await response.Content.ReadAsStringAsync(ct);
                var raw = ExtractAssistantContent(responseJson);
                var json = ExtractJsonObject(raw);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return null;
                }

                try
                {
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    var index = root.TryGetProperty("index", out var idxEl) ? idxEl.GetInt32() : -1;
                    var confidence = root.TryGetProperty("confidence", out var confEl) ? confEl.GetDouble() : 0.0;
                    if (index < 0 || index >= candidates.Count)
                    {
                        return null;
                    }

                    var selected = candidates[index].Product!;
                    return new CatalogAiSelection(selected, confidence);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "CruzVerde: respuesta OpenAI invalida.");
                    return null;
                }
            }

            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests && attempt < maxRetries)
            {
                var delay = ResolveRetryDelay(response, retryBaseDelayMs, attempt);
                Logger.LogWarning("CruzVerde: OpenAI 429, reintentando en {DelayMs}ms.", delay.TotalMilliseconds);
                await Task.Delay(delay, ct);
                continue;
            }

            Logger.LogWarning("CruzVerde: OpenAI fallo {Status}", response.StatusCode);
            return null;
        }

        return null;
    }

    private static string BuildCatalogAiPrompt(string description, List<CatalogCandidateResult> candidates)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Producto catalogo:");
        builder.AppendLine(description);
        builder.AppendLine();
        builder.AppendLine("Candidatos:");
        for (var i = 0; i < candidates.Count; i += 1)
        {
            var candidate = candidates[i].Product;
            builder.AppendLine($"[{i}] Marca: {candidate.Brand} | Nombre: {candidate.Name} | Url: {candidate.Url} | Sku: {candidate.CompetitorSku} | Lista: {candidate.ListPrice} | Promo: {candidate.PromoPrice}");
        }

        return builder.ToString();
    }

    private async Task<bool> PersistCatalogMatchAsync(
        CompetitorDb db,
        AdapterContext context,
        ProductRow product,
        CompetitorDb.CompetitorCatalogRow hit,
        string apiBase,
        string loginUrl,
        string inventoryId,
        int delayMs,
        string matchMethod,
        double matchScore,
        CancellationToken ct)
    {
        var url = hit.Url;
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        var productId = !string.IsNullOrWhiteSpace(hit.CompetitorSku)
            ? hit.CompetitorSku
            : ExtractProductId(url);
        if (string.IsNullOrWhiteSpace(productId))
        {
            return false;
        }

        var listPrice = hit.ListPrice;
        var promoPrice = hit.PromoPrice;

        var canReuseCachedPrices = hit.ExtractedAt.HasValue &&
            hit.ExtractedAt.Value.Date == context.RunDate.Date &&
            (listPrice.HasValue || promoPrice.HasValue);

        var name = hit.Name;
        var brand = hit.Brand;
        var pageUrl = (string?)null;

        if (!canReuseCachedPrices)
        {
            var summary = await GetProductSummaryAsync(apiBase, loginUrl, inventoryId, productId!, delayMs, ct);
            if (summary is null)
            {
                return false;
            }

            name = summary.Name ?? name;
            brand = summary.Brand ?? brand;
            pageUrl = summary.PageUrl;
            var freshUrl = BuildProductUrl(context.BaseUrl, productId!, pageUrl);
            if (!string.IsNullOrWhiteSpace(freshUrl))
            {
                url = freshUrl;
            }

            var prices = ResolvePrices(summary.Prices);
            listPrice = prices.ListPrice;
            promoPrice = prices.PromoPrice;

            await db.UpsertCompetitorCatalogAsync(
                context.CompetitorId,
                url,
                name,
                hit.Description,
                hit.Ean,
                productId!,
                brand,
                hit.Categories,
                listPrice,
                promoPrice,
                DateTime.UtcNow,
                ct
            );
        }

        await db.UpsertCompetitorProductAsync(
            product.Id,
            context.CompetitorId,
            url,
            name,
            matchMethod,
            (decimal)matchScore,
            DateTime.UtcNow,
            ct
        );

        if (matchMethod == "ean" && !string.IsNullOrWhiteSpace(product.Ean))
        {
            await db.UpsertCompetitorCatalogAsync(
                context.CompetitorId,
                url,
                name,
                hit.Description,
                NormalizeEan(product.Ean),
                productId!,
                brand,
                hit.Categories,
                listPrice,
                promoPrice,
                DateTime.UtcNow,
                ct
            );
        }

        await db.UpsertPriceSnapshotAsync(
            product.Id,
            context.CompetitorId,
            context.RunDate.Date,
            listPrice,
            promoPrice,
            ct
        );

        return true;
    }

    private static string? NormalizeEan(string? ean)
    {
        if (string.IsNullOrWhiteSpace(ean))
        {
            return null;
        }

        var digits = new string(ean.Where(char.IsDigit).ToArray());
        return string.IsNullOrWhiteSpace(digits) ? null : digits;
    }

    private static string? NormalizeBrandKey(string? brand)
    {
        if (string.IsNullOrWhiteSpace(brand))
        {
            return null;
        }

        var cleaned = Regex.Replace(brand, @"^\s*\d+\s*-\s*", string.Empty);
        var normalized = NormalizeText(cleaned);
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string NormalizeBrandForSearch(string brand)
    {
        // Brands are stored like "0041 - LA ROCHE". Search works better without the numeric prefix.
        var cleaned = Regex.Replace(brand.Trim(), @"^\s*\d+\s*-\s*", string.Empty);
        return cleaned.Trim();
    }

    private async Task<AiSelection?> TrySelectWithAiAsync(
        string description,
        List<CandidateResult> ranked,
        int maxCandidates,
        CancellationToken ct)
    {
        var apiKey = ResolveOpenAiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Logger.LogWarning("CruzVerde: OpenAI API key no configurada.");
            return null;
        }

        var maxRequestsPerMinute = Configuration.GetValue<int?>("Adapters:CruzVerde:AiMaxRequestsPerMinute") ?? 10;
        var maxRetries = Configuration.GetValue<int?>("Adapters:CruzVerde:AiMaxRetries") ?? 3;
        var retryBaseDelayMs = Configuration.GetValue<int?>("Adapters:CruzVerde:AiRetryBaseDelayMs") ?? 5000;

        var candidates = ranked
            .Where(r => r.Product is not null)
            .Take(Math.Max(1, maxCandidates))
            .ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        var model = Configuration.GetValue<string>("Adapters:CruzVerde:AiModel") ?? "gpt-4o-mini";
        var payload = new
        {
            model,
            temperature = 0,
            messages = new[]
            {
                new
                {
                    role = "system",
                    content = "Eres un asistente que empareja productos. Elige el mejor candidato. Responde SOLO JSON: {\"index\":number,\"confidence\":number,\"reason\":string}. Si ninguno coincide, usa index -1 y confidence 0."
                },
                new
                {
                    role = "user",
                    content = BuildAiPrompt(description, candidates)
                }
            }
        };

        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        for (var attempt = 0; attempt <= maxRetries; attempt += 1)
        {
            await EnforceAiRateLimitAsync(maxRequestsPerMinute, ct);

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var response = await http.PostAsync("https://api.openai.com/v1/chat/completions", content, ct);
            if (response.IsSuccessStatusCode)
            {
                var responseJson = await response.Content.ReadAsStringAsync(ct);
                var raw = ExtractAssistantContent(responseJson);
                var json = ExtractJsonObject(raw);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return null;
                }

                try
                {
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    var index = root.TryGetProperty("index", out var idxEl) ? idxEl.GetInt32() : -1;
                    var confidence = root.TryGetProperty("confidence", out var confEl) ? confEl.GetDouble() : 0.0;
                    if (index < 0 || index >= candidates.Count)
                    {
                        return null;
                    }

                    var selected = candidates[index].Product!;
                    return new AiSelection(selected, confidence);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "CruzVerde: respuesta OpenAI invalida.");
                    return null;
                }
            }

            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests && attempt < maxRetries)
            {
                var delay = ResolveRetryDelay(response, retryBaseDelayMs, attempt);
                Logger.LogWarning("CruzVerde: OpenAI 429, reintentando en {DelayMs}ms.", delay.TotalMilliseconds);
                await Task.Delay(delay, ct);
                continue;
            }

            Logger.LogWarning("CruzVerde: OpenAI fallo {Status}", response.StatusCode);
            return null;
        }

        return null;
    }

    private static TimeSpan ResolveRetryDelay(HttpResponseMessage response, int baseDelayMs, int attempt)
    {
        if (response.Headers.TryGetValues("Retry-After", out var values))
        {
            var value = values.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(value))
            {
                if (int.TryParse(value, out var seconds))
                {
                    return TimeSpan.FromSeconds(Math.Max(1, seconds));
                }

                if (DateTimeOffset.TryParse(value, out var date))
                {
                    var delta = date - DateTimeOffset.UtcNow;
                    if (delta.TotalMilliseconds > 0)
                    {
                        return delta;
                    }
                }
            }
        }

        var multiplier = Math.Pow(2, Math.Min(attempt, 6));
        var ms = Math.Min(baseDelayMs * multiplier, 60000);
        return TimeSpan.FromMilliseconds(ms);
    }

    private static async Task EnforceAiRateLimitAsync(int maxPerMinute, CancellationToken ct)
    {
        if (maxPerMinute <= 0)
        {
            return;
        }

        while (true)
        {
            TimeSpan delay;
            lock (AiRateLock)
            {
                var now = DateTime.UtcNow;
                if (AiWindowStartUtc == DateTime.MinValue || now - AiWindowStartUtc >= TimeSpan.FromMinutes(1))
                {
                    AiWindowStartUtc = now;
                    AiWindowCount = 0;
                }

                if (AiWindowCount < maxPerMinute)
                {
                    AiWindowCount += 1;
                    return;
                }

                delay = (AiWindowStartUtc + TimeSpan.FromMinutes(1)) - now;
                if (delay.TotalMilliseconds < 0)
                {
                    delay = TimeSpan.FromMilliseconds(500);
                }
            }

            await Task.Delay(delay, ct);
        }
    }

    private string? ResolveOpenAiKey()
    {
        var key = Configuration["OpenAI:ApiKey"];
        if (!string.IsNullOrWhiteSpace(key))
        {
            return key;
        }

        key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (!string.IsNullOrWhiteSpace(key))
        {
            return key;
        }

        return null;
    }

    private static string BuildAiPrompt(string description, List<CandidateResult> candidates)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Producto catalogo:");
        builder.AppendLine(description);
        builder.AppendLine();
        builder.AppendLine("Candidatos:");
        for (var i = 0; i < candidates.Count; i += 1)
        {
            var candidate = candidates[i].Product!;
            builder.AppendLine($"[{i}] Marca: {candidate.Brand} | Nombre: {candidate.ProductName} | Id: {candidate.ProductId} | Lista: {candidate.Prices?.ListPrice} | Promo: {candidate.Prices?.SalePrice}");
        }

        return builder.ToString();
    }

    private static string? ExtractAssistantContent(string responseJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;
            var choice = root.GetProperty("choices")[0];
            var message = choice.GetProperty("message");
            return message.GetProperty("content").GetString();
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractJsonObject(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        return text.Substring(start, end - start + 1);
    }

    private sealed record PriceValues(decimal? ListPrice, decimal? PromoPrice);
    private sealed record SearchResult(bool Success, List<SearchHit> Hits);
    private sealed record SearchPagedResult(bool Success, int? Total, List<SearchHit> Hits);
    private sealed record SelectionResult(SearchHit Product, double Score, string Method);
    private sealed record CandidateResult(SearchHit? Product, double Score);
    private sealed record AiSelection(SearchHit Product, double Confidence);
    private sealed record CatalogSelectionResult(CompetitorDb.CompetitorCatalogRow Product, double Score, string Method);
    private sealed record CatalogCandidateResult(CompetitorDb.CompetitorCatalogRow Product, double Score);
    private sealed record CatalogAiSelection(CompetitorDb.CompetitorCatalogRow Product, double Confidence);

    private enum MatchOutcome
    {
        Matched,
        NoMatch,
        Error
    }

    private sealed class Counters
    {
        public int Processed { get; set; }
        public int Created { get; set; }
        public int Updated { get; set; }
        public int Errors { get; set; }
    }

    private sealed record SearchResponse(
        [property: JsonPropertyName("total")] int? Total,
        [property: JsonPropertyName("hits")] List<SearchHit>? Hits
    );

    private sealed record SearchHit(
        [property: JsonPropertyName("productId")] string? ProductId,
        [property: JsonPropertyName("productName")] string? ProductName,
        [property: JsonPropertyName("brand")] string? Brand,
        [property: JsonPropertyName("pageURL")] string? PageUrl,
        [property: JsonPropertyName("prices")] PriceMap? Prices
    );

    private sealed record ProductSummaryItem(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("brand")] string? Brand,
        [property: JsonPropertyName("pageURL")] string? PageUrl,
        [property: JsonPropertyName("prices")] PriceMap? Prices
    );

    private sealed record PriceMap(
        [property: JsonPropertyName("price-list-col")] decimal? ListPrice,
        [property: JsonPropertyName("price-sale-col")] decimal? SalePrice
    );
}
