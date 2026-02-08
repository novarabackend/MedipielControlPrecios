using System.Globalization;
using System.Text.RegularExpressions;
using System.Text;
using HtmlAgilityPack;
using Medipiel.Competitors.Abstractions;
using Medipiel.Competitors.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Medipiel.Competitors.LineaEstetica;

public sealed class LineaEsteticaAdapter : CompetitorAdapterBase, ICompetitorProductProbe
{
    private const string BrandsPath = "/marcas/";
    private const string BrandPathSegment = "/marca/";
    private const string ProductPathSegment = "/producto/";

    public LineaEsteticaAdapter(IConfiguration configuration, ILogger<LineaEsteticaAdapter> logger)
        : base(configuration, logger)
    {
    }

    public override string AdapterId => "lineaestetica";
    public override string Name => "Linea Estetica";

    public async Task<CompetitorProductProbeResult> ProbeAsync(CompetitorProductProbeRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ProductUrl))
        {
            throw new ArgumentException("ProductUrl is required.", nameof(request));
        }

        var delayMs = GetDelayMs("Adapters:LineaEstetica:DelayMs", 250);

        var html = await GetHtmlAsync(request.ProductUrl, delayMs, ct);
        if (string.IsNullOrWhiteSpace(html))
        {
            throw new InvalidOperationException("Empty response.");
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var priceNode = doc.DocumentNode.SelectSingleNode("//p[contains(@class,'price')]")
                        ?? doc.DocumentNode.SelectSingleNode("//*[contains(@class,'price')]//span[contains(@class,'amount')]")?.ParentNode;

        string? rawPromo = null;
        string? rawList = null;
        string? rawAmount = null;
        string? decodedPromo = null;
        string? decodedList = null;
        string? decodedAmount = null;

        if (priceNode is not null)
        {
            var promoNode = priceNode.SelectSingleNode(".//ins//span[contains(@class,'amount')]");
            var listNode = priceNode.SelectSingleNode(".//del//span[contains(@class,'amount')]");
            var amountNode = priceNode.SelectSingleNode(".//span[contains(@class,'amount')]");

            rawPromo = promoNode?.InnerText;
            rawList = listNode?.InnerText;
            rawAmount = amountNode?.InnerText;

            decodedPromo = rawPromo is null ? null : HtmlEntity.DeEntitize(rawPromo);
            decodedList = rawList is null ? null : HtmlEntity.DeEntitize(rawList);
            decodedAmount = rawAmount is null ? null : HtmlEntity.DeEntitize(rawAmount);
        }

        var prices = ExtractPrices(doc);
        return new CompetitorProductProbeResult(
            request.ProductUrl,
            prices.ListPrice,
            prices.PromoPrice,
            rawList,
            rawPromo,
            rawAmount,
            decodedList,
            decodedPromo,
            decodedAmount
        );
    }

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

        var counters = new Counters();
        var targets = products
            .Select(p => new { Row = p, Ean = NormalizeEan(p.Ean) })
            .Where(p => !string.IsNullOrWhiteSpace(p.Ean))
            .ToDictionary(p => p.Ean!, p => p.Row, StringComparer.OrdinalIgnoreCase);
        var eanKeyByProductId = targets.ToDictionary(x => x.Value.Id, x => x.Key);

        Logger.LogInformation(
            "LineaEstetica: inicio {Total} productos (EAN disponibles={Targets}).",
            products.Count,
            targets.Count
        );

        if (targets.Count == 0)
        {
            Logger.LogInformation("LineaEstetica: no hay productos con EAN para match, se construye catalogo completo.");
        }

        var baseUrl = context.BaseUrl.TrimEnd('/');
        var catalog = await CrawlCatalogAsync(
            db,
            context,
            baseUrl,
            delayMs,
            targets,
            counters,
            ct
        );

        if (targets.Count > 0 && catalog.Count > 0)
        {
            var matchedByName = await MatchByNameAsync(db, context, targets.Values.ToList(), catalog, counters, ct);
            if (matchedByName.Count > 0)
            {
                foreach (var matchedId in matchedByName)
                {
                    if (eanKeyByProductId.TryGetValue(matchedId, out var eanKey))
                    {
                        targets.Remove(eanKey);
                    }
                }
            }
        }

        if (targets.Count > 0)
        {
            Logger.LogInformation("LineaEstetica: no_match para {Count} productos.", targets.Count);
            foreach (var remaining in targets.Values)
            {
                await db.MarkNoMatchAsync(remaining.Id, context.CompetitorId, ct);
            }
        }

        await UpdateSnapshotsFromUrlsAsync(db, context, baseUrl, delayMs, ct);

        Logger.LogInformation(
            "LineaEstetica: fin Processed={Processed} Updated={Updated} Errors={Errors}.",
            counters.Processed,
            counters.Updated,
            counters.Errors
        );

        return new AdapterRunResult(
            counters.Processed,
            counters.Created,
            counters.Updated,
            counters.Errors,
            null
        );
    }

    private async Task<List<CatalogItem>> CrawlCatalogAsync(
        CompetitorDb db,
        AdapterContext context,
        string baseUrl,
        int delayMs,
        Dictionary<string, ProductRow> targets,
        Counters counters,
        CancellationToken ct)
    {
        var existingCatalog = await db.LoadCompetitorCatalogAsync(context.CompetitorId, ct);
        var catalogByUrl = existingCatalog.ToDictionary(x => x.Url, StringComparer.OrdinalIgnoreCase);
        var catalog = existingCatalog
            .Select(x => new CatalogItem(
                x.Url,
                x.Name,
                x.Description,
                x.Ean,
                x.CompetitorSku,
                x.Brand,
                x.Categories,
                x.ListPrice,
                x.PromoPrice
            ))
            .ToList();

        if (catalog.Count > 0)
        {
            Logger.LogInformation(
                "LineaEstetica: reutilizando catalogo existente ({Count} items).",
                catalog.Count
            );

            // First, match by EAN using the cached catalog (cheap and fast).
            var matchedFromCatalog = await MatchTargetsFromCatalogByEanAsync(db, context, targets, existingCatalog, ct);
            if (matchedFromCatalog > 0)
            {
                Logger.LogInformation("LineaEstetica: match por EAN desde catalogo cache={Matched}.", matchedFromCatalog);
            }

            if (context.OnlyNew && targets.Count == 0)
            {
                return catalog;
            }

            // Then, enrich missing EANs (bounded) to increase future EAN matches.
            var enrichMax = context.OnlyNew
                ? (Configuration.GetValue<int?>("Adapters:LineaEstetica:CatalogEnrichMissingEanMax") ?? 500)
                : int.MaxValue;

            if (enrichMax > 0 && (!context.OnlyNew || targets.Count > 0))
            {
                var enriched = await EnrichMissingEansAsync(
                    db,
                    context,
                    baseUrl,
                    delayMs,
                    targets,
                    existingCatalog,
                    catalogByUrl,
                    catalog,
                    enrichMax,
                    stopWhenNoTargets: context.OnlyNew,
                    counters,
                    ct
                );

                if (enriched > 0)
                {
                    Logger.LogInformation("LineaEstetica: catalogo enriquecido (EAN) items={Count}.", enriched);
                }
            }

            return catalog;
        }

        var brandLinks = await LoadBrandLinksAsync(baseUrl, delayMs, ct);
        Logger.LogInformation("LineaEstetica: marcas encontradas {Count}.", brandLinks.Count);

        var visitedProducts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var processed = 0;
        var stored = 0;

        foreach (var brandUrl in brandLinks)
        {
            var brandProducts = await LoadBrandProductLinksAsync(baseUrl, brandUrl, delayMs, ct);
            Logger.LogInformation(
                "LineaEstetica: marca {Brand} productos {Count}.",
                brandUrl,
                brandProducts.Count
            );

            foreach (var productUrl in brandProducts)
            {
                if (!visitedProducts.Add(productUrl))
                {
                    continue;
                }

                var item = await LoadProductAsync(baseUrl, productUrl, delayMs, ct);
                if (item is null)
                {
                    counters.Errors += 1;
                    continue;
                }

                await db.UpsertCompetitorCatalogAsync(
                    context.CompetitorId,
                    item.Url,
                    item.Name,
                    item.Description,
                    item.Ean,
                    item.CompetitorSku,
                    item.Brand,
                    item.Categories,
                    item.ListPrice,
                    item.PromoPrice,
                    DateTime.UtcNow,
                    ct
                );

                catalog.Add(item);
                stored += 1;
                processed += 1;

                catalogByUrl[item.Url] = new CompetitorDb.CompetitorCatalogRow(
                    item.Url,
                    item.Name,
                    item.Description,
                    item.Ean,
                    item.CompetitorSku,
                    item.Brand,
                    item.Categories,
                    item.ListPrice,
                    item.PromoPrice,
                    DateTime.UtcNow
                );

                var eanKey = NormalizeEan(item.Ean);
                if (!string.IsNullOrWhiteSpace(eanKey) && targets.TryGetValue(eanKey!, out var product))
                {
                    await db.UpsertCompetitorProductAsync(
                        product.Id,
                        context.CompetitorId,
                        item.Url,
                        item.Name,
                        "ean",
                        1,
                        DateTime.UtcNow,
                        ct
                    );

                    await db.UpsertPriceSnapshotAsync(
                        product.Id,
                        context.CompetitorId,
                        context.RunDate.Date,
                        item.ListPrice,
                        item.PromoPrice,
                        ct
                    );

                    counters.Processed += 1;
                    counters.Updated += 1;
                    targets.Remove(eanKey!);
                }

                if (processed % 100 == 0)
                {
                    Logger.LogInformation("LineaEstetica: productos procesados {Count}.", processed);
                }
            }
        }

        Logger.LogInformation(
            "LineaEstetica: catalogo completo. Productos unicos={TotalUrls}, guardados={Stored}.",
            visitedProducts.Count,
            stored
        );

        if (stored > 0)
        {
            catalog = catalogByUrl.Values
                .Select(x => new CatalogItem(
                    x.Url,
                    x.Name,
                    x.Description,
                    x.Ean,
                    x.CompetitorSku,
                    x.Brand,
                    x.Categories,
                    x.ListPrice,
                    x.PromoPrice
                ))
                .ToList();
        }

        return catalog;
    }

    private static async Task<int> MatchTargetsFromCatalogByEanAsync(
        CompetitorDb db,
        AdapterContext context,
        Dictionary<string, ProductRow> targets,
        List<CompetitorDb.CompetitorCatalogRow> existingCatalog,
        CancellationToken ct)
    {
        if (targets.Count == 0 || existingCatalog.Count == 0)
        {
            return 0;
        }

        var map = existingCatalog
            .Select(row => new { Row = row, Ean = NormalizeEan(row.Ean) })
            .Where(x => !string.IsNullOrWhiteSpace(x.Ean))
            .GroupBy(x => x.Ean!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.First().Row, StringComparer.OrdinalIgnoreCase);

        if (map.Count == 0)
        {
            return 0;
        }

        var matched = 0;
        foreach (var ean in targets.Keys.ToList())
        {
            if (!map.TryGetValue(ean, out var hit))
            {
                continue;
            }

            var product = targets[ean];
            await db.UpsertCompetitorProductAsync(
                product.Id,
                context.CompetitorId,
                hit.Url,
                hit.Name,
                "ean",
                1,
                DateTime.UtcNow,
                ct
            );

            targets.Remove(ean);
            matched += 1;
        }

        return matched;
    }

    private async Task<int> EnrichMissingEansAsync(
        CompetitorDb db,
        AdapterContext context,
        string baseUrl,
        int delayMs,
        Dictionary<string, ProductRow> targets,
        List<CompetitorDb.CompetitorCatalogRow> existingCatalog,
        Dictionary<string, CompetitorDb.CompetitorCatalogRow> catalogByUrl,
        List<CatalogItem> catalog,
        int enrichMax,
        bool stopWhenNoTargets,
        Counters counters,
        CancellationToken ct)
    {
        var missing = existingCatalog
            .Where(x => string.IsNullOrWhiteSpace(NormalizeEan(x.Ean)))
            .OrderBy(x => x.ExtractedAt ?? DateTime.MinValue)
            .Take(enrichMax)
            .ToList();

        if (missing.Count == 0)
        {
            return 0;
        }

        var enriched = 0;
        foreach (var row in missing)
        {
            if (stopWhenNoTargets && targets.Count == 0)
            {
                break;
            }

            var item = await LoadProductAsync(baseUrl, row.Url, delayMs, ct);
            if (item is null)
            {
                counters.Errors += 1;
                continue;
            }

            var eanKey = NormalizeEan(item.Ean);
            await db.UpsertCompetitorCatalogAsync(
                context.CompetitorId,
                item.Url,
                item.Name,
                item.Description,
                eanKey,
                item.CompetitorSku,
                item.Brand,
                item.Categories,
                item.ListPrice,
                item.PromoPrice,
                DateTime.UtcNow,
                ct
            );

            // Keep in-memory catalog updated for name matching in the same run.
            catalogByUrl[item.Url] = new CompetitorDb.CompetitorCatalogRow(
                item.Url,
                item.Name,
                item.Description,
                eanKey,
                item.CompetitorSku,
                item.Brand,
                item.Categories,
                item.ListPrice,
                item.PromoPrice,
                DateTime.UtcNow
            );

            // Refresh the list used for name matching.
            // We keep the first occurrence and don't duplicate.
            if (!catalog.Any(x => x.Url.Equals(item.Url, StringComparison.OrdinalIgnoreCase)))
            {
                catalog.Add(item);
            }

            enriched += 1;

            // If we found an EAN match, persist mapping and snapshot with the fresh prices.
            if (!string.IsNullOrWhiteSpace(eanKey) && targets.TryGetValue(eanKey!, out var product))
            {
                await db.UpsertCompetitorProductAsync(
                    product.Id,
                    context.CompetitorId,
                    item.Url,
                    item.Name,
                    "ean",
                    1,
                    DateTime.UtcNow,
                    ct
                );

                await db.UpsertPriceSnapshotAsync(
                    product.Id,
                    context.CompetitorId,
                    context.RunDate.Date,
                    item.ListPrice,
                    item.PromoPrice,
                    ct
                );

                counters.Processed += 1;
                counters.Updated += 1;
                targets.Remove(eanKey!);
            }

            if (enriched % 100 == 0)
            {
                Logger.LogInformation("LineaEstetica: EAN enriquecidos {Count}.", enriched);
            }
        }

        return enriched;
    }

    private async Task UpdateSnapshotsFromUrlsAsync(
        CompetitorDb db,
        AdapterContext context,
        string baseUrl,
        int delayMs,
        CancellationToken ct)
    {
        var priceTargets = await db.LoadProductsAsync(
            context.CompetitorId,
            context.RunDate,
            context.OnlyNew,
            context.BatchSize,
            requireEan: true,
            ct
        );

        var targetsWithUrl = priceTargets
            .Where(p => !string.IsNullOrWhiteSpace(p.Url))
            .ToList();

        if (targetsWithUrl.Count == 0)
        {
            return;
        }

        Logger.LogInformation(
            "LineaEstetica: actualizando precios para {Count} productos.",
            targetsWithUrl.Count
        );

        foreach (var product in targetsWithUrl)
        {
            var item = await LoadProductAsync(baseUrl, product.Url!, delayMs, ct);
            if (item is null)
            {
                continue;
            }

            await db.UpsertCompetitorCatalogAsync(
                context.CompetitorId,
                item.Url,
                item.Name,
                item.Description,
                item.Ean,
                item.CompetitorSku,
                item.Brand,
                item.Categories,
                item.ListPrice,
                item.PromoPrice,
                DateTime.UtcNow,
                ct
            );

            await db.UpsertPriceSnapshotAsync(
                product.Id,
                context.CompetitorId,
                context.RunDate.Date,
                item.ListPrice,
                item.PromoPrice,
                ct
            );
        }
    }

    private async Task<HashSet<int>> MatchByNameAsync(
        CompetitorDb db,
        AdapterContext context,
        List<ProductRow> products,
        List<CatalogItem> catalog,
        Counters counters,
        CancellationToken ct)
    {
        var minScore = Configuration.GetValue<double?>("Adapters:LineaEstetica:NameMinScore") ?? 0.62;
        var minGap = Configuration.GetValue<double?>("Adapters:LineaEstetica:NameMinGap") ?? 0.05;
        var highConfidenceScore = Configuration.GetValue<double?>("Adapters:LineaEstetica:NameHighConfidenceScore") ?? 0.75;
        var debugTop = Configuration.GetValue<int?>("Adapters:LineaEstetica:NameDebugTop") ?? 0;

        var catalogIndex = catalog
            .Select(item =>
            {
                var baseTokens = BuildTokens(item.Name, item.Description);
                var brandTokens = BuildTokens(item.Brand);
                var merged = MergeTokens(baseTokens, brandTokens, includeSecondaryNumeric: false);
                return new CatalogIndexItem(item, merged);
            })
            .Where(x => x.Tokens.Tokens.Count > 0)
            .ToList();

        var usedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matchedIds = new HashSet<int>();
        var matched = 0;
        var debugLogged = 0;

        foreach (var product in products)
        {
            var descTokens = BuildTokens(product.Description);
            var brandTokens = BuildTokens(product.BrandName);
            var productTokens = MergeTokens(descTokens, brandTokens, includeSecondaryNumeric: false);
            if (productTokens.Tokens.Count == 0)
            {
                continue;
            }

            var brandFilter = brandTokens.Tokens.Where(t => !t.All(char.IsDigit)).ToHashSet(StringComparer.OrdinalIgnoreCase);

            CatalogIndexItem? best = null;
            var bestScore = 0.0;
            var secondScore = 0.0;

            var candidates = catalogIndex;
            if (brandFilter.Count > 0)
            {
                var byBrand = catalogIndex.Where(c => c.Tokens.Tokens.Overlaps(brandFilter)).ToList();
                if (byBrand.Count > 0)
                {
                    candidates = byBrand;
                }
            }

            // If we still have too many candidates, use numeric (presentation) to reduce ambiguity.
            // Use only numeric from the product description to avoid filtering by internal codes in BrandName.
            if (descTokens.Numeric.Count > 0 && candidates.Count > 150)
            {
                var byNumeric = candidates.Where(c => c.Tokens.Numeric.Overlaps(descTokens.Numeric)).ToList();
                if (byNumeric.Count > 0)
                {
                    candidates = byNumeric;
                }
            }

            foreach (var candidate in candidates)
            {
                if (usedUrls.Contains(candidate.Item.Url))
                {
                    continue;
                }

                var score = ComputeScore(productTokens, candidate.Tokens);
                if (score <= bestScore)
                {
                    if (score > secondScore)
                    {
                        secondScore = score;
                    }
                    continue;
                }

                secondScore = bestScore;
                bestScore = score;
                best = candidate;
            }

            if (best is null)
            {
                continue;
            }

            if (debugTop > 0 && debugLogged < debugTop)
            {
                Logger.LogInformation(
                    "LineaEstetica: name debug ProductId={ProductId} Ean={Ean} Brand={Brand} Desc={Desc} Candidates={Candidates} BestScore={BestScore} SecondScore={SecondScore} BestName={BestName} BestUrl={BestUrl}",
                    product.Id,
                    product.Ean,
                    product.BrandName,
                    product.Description,
                    candidates.Count,
                    bestScore,
                    secondScore,
                    best.Item.Name,
                    best.Item.Url
                );
                debugLogged += 1;
            }

            if (bestScore < minScore || ((bestScore - secondScore) < minGap && bestScore < highConfidenceScore))
            {
                continue;
            }

            await db.UpsertCompetitorProductAsync(
                product.Id,
                context.CompetitorId,
                best.Item.Url,
                best.Item.Name,
                "name",
                (decimal)bestScore,
                DateTime.UtcNow,
                ct
            );

            counters.Processed += 1;
            counters.Updated += 1;
            usedUrls.Add(best.Item.Url);
            matchedIds.Add(product.Id);
            matched += 1;
        }

        if (matched > 0)
        {
            Logger.LogInformation("LineaEstetica: match por nombre={Matched}.", matched);
        }

        return matchedIds;
    }

    private static TokenSet BuildTokens(params string?[] values)
    {
        var combined = string.Join(' ', values.Where(v => !string.IsNullOrWhiteSpace(v)));
        if (string.IsNullOrWhiteSpace(combined))
        {
            return new TokenSet(new HashSet<string>(), new HashSet<string>());
        }

        var normalized = RemoveDiacritics(combined.ToLowerInvariant());
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch))
            {
                sb.Append(ch);
            }
            else
            {
                sb.Append(' ');
            }
        }

        var tokens = new HashSet<string>();
        var numeric = new HashSet<string>();
        foreach (var raw in sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (raw.Length <= 1)
            {
                continue;
            }

            AddToken(tokens, numeric, raw);
        }

        return new TokenSet(tokens, numeric);
    }

    private static TokenSet MergeTokens(TokenSet primary, TokenSet secondary, bool includeSecondaryNumeric)
    {
        var tokens = new HashSet<string>(primary.Tokens, StringComparer.OrdinalIgnoreCase);
        foreach (var token in secondary.Tokens)
        {
            if (!includeSecondaryNumeric && token.All(char.IsDigit))
            {
                continue;
            }

            tokens.Add(token);
        }

        var numeric = new HashSet<string>(primary.Numeric, StringComparer.OrdinalIgnoreCase);
        if (includeSecondaryNumeric)
        {
            numeric.UnionWith(secondary.Numeric);
        }

        return new TokenSet(tokens, numeric);
    }

    private static void AddToken(HashSet<string> tokens, HashSet<string> numeric, string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        var hasLetter = false;
        var hasDigit = false;
        foreach (var ch in raw)
        {
            if (char.IsLetter(ch))
            {
                hasLetter = true;
            }
            else if (char.IsDigit(ch))
            {
                hasDigit = true;
            }
        }

        // Keep "b5", "b3" as a single token (useful in cosmetics).
        var keepRawAlnum = raw.Length <= 3 && hasLetter && hasDigit &&
                           raw.Count(char.IsLetter) == 1 &&
                           raw.Count(char.IsDigit) == 1;

        if (!hasLetter || !hasDigit)
        {
            if (!Stopwords.Contains(raw))
            {
                tokens.Add(raw);
            }

            if (raw.All(char.IsDigit))
            {
                numeric.Add(raw);
            }

            return;
        }

        if (keepRawAlnum && !Stopwords.Contains(raw))
        {
            tokens.Add(raw);
        }

        foreach (var part in SplitAlphaNumeric(raw))
        {
            if (part.Length == 0)
            {
                continue;
            }

            if (part.All(char.IsDigit))
            {
                // Numeric matters even when single digit (e.g., b5).
                numeric.Add(part);
                if (part.Length > 1)
                {
                    tokens.Add(part);
                }

                continue;
            }

            if (part.Length <= 1)
            {
                continue;
            }

            if (Stopwords.Contains(part))
            {
                continue;
            }

            tokens.Add(part);
        }
    }

    private static IEnumerable<string> SplitAlphaNumeric(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            yield break;
        }

        var sb = new StringBuilder(token.Length);
        var prevIsDigit = char.IsDigit(token[0]);
        sb.Append(token[0]);

        for (var i = 1; i < token.Length; i += 1)
        {
            var ch = token[i];
            var isDigit = char.IsDigit(ch);
            if (isDigit != prevIsDigit)
            {
                yield return sb.ToString();
                sb.Clear();
                prevIsDigit = isDigit;
            }

            sb.Append(ch);
        }

        if (sb.Length > 0)
        {
            yield return sb.ToString();
        }
    }

    private static double ComputeScore(TokenSet a, TokenSet b)
    {
        if (a.Tokens.Count == 0 || b.Tokens.Count == 0)
        {
            return 0;
        }

        var aWords = a.Tokens.Where(t => !t.All(char.IsDigit)).ToList();
        var bWords = b.Tokens.Where(t => !t.All(char.IsDigit)).ToList();
        if (aWords.Count == 0 || bWords.Count == 0)
        {
            return 0;
        }

        var wordScore = SymmetricAverageSimilarity(aWords, bWords);
        if (wordScore <= 0)
        {
            return 0;
        }

        var numericScore = 0.0;
        if (a.Numeric.Count > 0 && b.Numeric.Count > 0)
        {
            var commonNumeric = a.Numeric.Intersect(b.Numeric).Count();
            numericScore = commonNumeric / (double)Math.Max(a.Numeric.Count, b.Numeric.Count);
        }

        if (numericScore <= 0)
        {
            return wordScore;
        }

        return (wordScore * 0.75) + (numericScore * 0.25);
    }

    private static double SymmetricAverageSimilarity(List<string> a, List<string> b)
    {
        if (a.Count == 0 || b.Count == 0)
        {
            return 0.0;
        }

        var ab = AverageBestSimilarity(a, b);
        var ba = AverageBestSimilarity(b, a);
        return (ab + ba) / 2.0;
    }

    private static double AverageBestSimilarity(List<string> source, List<string> candidates)
    {
        if (source.Count == 0 || candidates.Count == 0)
        {
            return 0.0;
        }

        var sum = 0.0;
        foreach (var token in source)
        {
            var best = 0.0;
            foreach (var candidate in candidates)
            {
                var sim = TokenSimilarity(token, candidate);
                if (sim > best)
                {
                    best = sim;
                }

                if (best >= 1.0)
                {
                    break;
                }
            }

            // Ignore very weak similarity for short/noisy tokens.
            var threshold = SimilarityThreshold(token);
            sum += best >= threshold ? best : 0.0;
        }

        return sum / source.Count;
    }

    private static double SimilarityThreshold(string token)
    {
        // Short tokens are noisy; be strict.
        if (token.Length <= 3)
        {
            return 1.0;
        }

        if (token.Length == 4)
        {
            return 0.93;
        }

        return 0.90;
    }

    private static double TokenSimilarity(string a, string b)
    {
        if (a.Equals(b, StringComparison.OrdinalIgnoreCase))
        {
            return 1.0;
        }

        // Prefix match catches abbreviations like "acondicio" -> "acondicionador".
        var minLen = Math.Min(a.Length, b.Length);
        if (minLen >= 4)
        {
            if (a.StartsWith(b, StringComparison.OrdinalIgnoreCase) || b.StartsWith(a, StringComparison.OrdinalIgnoreCase))
            {
                return 0.96;
            }
        }

        return JaroWinkler(a, b);
    }

    private static double JaroWinkler(string s1, string s2)
    {
        if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2))
        {
            return 0.0;
        }

        s1 = s1.ToLowerInvariant();
        s2 = s2.ToLowerInvariant();

        var len1 = s1.Length;
        var len2 = s2.Length;
        var matchDistance = Math.Max(len1, len2) / 2 - 1;
        if (matchDistance < 0)
        {
            matchDistance = 0;
        }

        var s1Matches = new bool[len1];
        var s2Matches = new bool[len2];

        var matches = 0;
        for (var i = 0; i < len1; i += 1)
        {
            var start = Math.Max(0, i - matchDistance);
            var end = Math.Min(i + matchDistance + 1, len2);
            for (var j = start; j < end; j += 1)
            {
                if (s2Matches[j])
                {
                    continue;
                }

                if (s1[i] != s2[j])
                {
                    continue;
                }

                s1Matches[i] = true;
                s2Matches[j] = true;
                matches += 1;
                break;
            }
        }

        if (matches == 0)
        {
            return 0.0;
        }

        var t = 0;
        var k = 0;
        for (var i = 0; i < len1; i += 1)
        {
            if (!s1Matches[i])
            {
                continue;
            }

            while (k < len2 && !s2Matches[k])
            {
                k += 1;
            }

            if (k < len2 && s1[i] != s2[k])
            {
                t += 1;
            }

            k += 1;
        }

        var transpositions = t / 2.0;
        var m = matches;
        var jaro = ((m / (double)len1) + (m / (double)len2) + ((m - transpositions) / m)) / 3.0;

        // Winkler prefix scaling (up to 4 chars)
        var prefix = 0;
        for (var i = 0; i < Math.Min(4, Math.Min(len1, len2)); i += 1)
        {
            if (s1[i] == s2[i])
            {
                prefix += 1;
            }
            else
            {
                break;
            }
        }

        const double scaling = 0.1;
        return jaro + (prefix * scaling * (1.0 - jaro));
    }

    private static string RemoveDiacritics(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);

        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private async Task<List<string>> LoadBrandLinksAsync(string baseUrl, int delayMs, CancellationToken ct)
    {
        var url = baseUrl + BrandsPath;
        var html = await GetHtmlAsync(url, delayMs, ct);
        if (string.IsNullOrWhiteSpace(html))
        {
            return new List<string>();
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var links = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nodes = doc.DocumentNode.SelectNodes("//a[contains(@href, '/marca/')]");
        if (nodes is null)
        {
            return links.ToList();
        }

        foreach (var node in nodes)
        {
            var href = node.GetAttributeValue("href", string.Empty);
            if (string.IsNullOrWhiteSpace(href))
            {
                continue;
            }

            var absolute = NormalizeUrl(baseUrl, href).TrimEnd('/');
            if (!IsBrandUrl(absolute))
            {
                continue;
            }

            links.Add(absolute + "/");
        }

        return links.ToList();
    }

    private async Task<List<string>> LoadBrandProductLinksAsync(
        string baseUrl,
        string brandUrl,
        int delayMs,
        CancellationToken ct)
    {
        var products = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pageUrl = brandUrl;

        while (!string.IsNullOrWhiteSpace(pageUrl) && visited.Add(pageUrl))
        {
            var html = await GetHtmlAsync(pageUrl, delayMs, ct);
            if (string.IsNullOrWhiteSpace(html))
            {
                break;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var nodes = doc.DocumentNode.SelectNodes("//a[contains(@href, '/producto/')]");
            if (nodes is not null)
            {
                foreach (var node in nodes)
                {
                    var href = node.GetAttributeValue("href", string.Empty);
                    if (string.IsNullOrWhiteSpace(href))
                    {
                        continue;
                    }

                    var absolute = NormalizeUrl(baseUrl, href).TrimEnd('/');
                    if (!IsProductUrl(absolute))
                    {
                        continue;
                    }

                    products.Add(absolute + "/");
                }
            }

            pageUrl = GetNextPageUrl(doc, baseUrl);
        }

        return products.ToList();
    }

    private async Task<CatalogItem?> LoadProductAsync(
        string baseUrl,
        string productUrl,
        int delayMs,
        CancellationToken ct)
    {
        var html = await GetHtmlAsync(productUrl, delayMs, ct);
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var name = ExtractTitle(doc);
        var description = ExtractDescription(doc);
        var ean = ExtractEan(doc);
        var competitorSku = ExtractCompetitorSku(doc);
        var brand = ExtractBrand(doc);
        var categories = ExtractCategories(doc);
        var prices = ExtractPrices(doc);

        return new CatalogItem(
            productUrl,
            name,
            description,
            ean,
            competitorSku,
            brand,
            categories,
            prices.ListPrice,
            prices.PromoPrice
        );
    }

    private static bool IsBrandUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var path = uri.AbsolutePath.TrimEnd('/') + "/";
        return path.StartsWith(BrandPathSegment, StringComparison.OrdinalIgnoreCase) &&
               !path.Equals(BrandPathSegment, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsProductUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var path = uri.AbsolutePath.TrimEnd('/') + "/";
        return path.Contains(ProductPathSegment, StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetNextPageUrl(HtmlDocument doc, string baseUrl)
    {
        var nextNode = doc.DocumentNode
            .SelectSingleNode("//nav[contains(@class,'pagination')]//a[contains(@class,'next')]")
            ?? doc.DocumentNode.SelectSingleNode("//a[contains(.,'Siguiente')]");

        if (nextNode is null)
        {
            return null;
        }

        var href = nextNode.GetAttributeValue("href", string.Empty);
        if (string.IsNullOrWhiteSpace(href))
        {
            return null;
        }

        return NormalizeUrl(baseUrl, href).TrimEnd('/') + "/";
    }

    private static string? ExtractTitle(HtmlDocument doc)
    {
        var node = doc.DocumentNode.SelectSingleNode("//h1[contains(@class,'product_title')]")
                   ?? doc.DocumentNode.SelectSingleNode("//h1");
        if (node is null)
        {
            return null;
        }

        var text = HtmlEntity.DeEntitize(node.InnerText);
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static string? ExtractDescription(HtmlDocument doc)
    {
        var node = doc.DocumentNode.SelectSingleNode("//div[contains(@class,'woocommerce-product-details__short-description')]");
        if (node is null)
        {
            return null;
        }

        var text = HtmlEntity.DeEntitize(node.InnerText);
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static string? ExtractEan(HtmlDocument doc)
    {
        var rows = doc.DocumentNode.SelectNodes("//table//tr");
        if (rows is null)
        {
            return null;
        }

        foreach (var row in rows)
        {
            var header = row.SelectSingleNode("./th") ?? row.SelectSingleNode("./td[1]");
            if (header is null)
            {
                continue;
            }

            var headerText = HtmlEntity.DeEntitize(header.InnerText).Trim();
            if (!IsBarcodeHeader(headerText))
            {
                continue;
            }

            var cell = row.SelectSingleNode("./td") ?? row.SelectSingleNode("./td[2]");
            if (cell is null)
            {
                return null;
            }

            var value = HtmlEntity.DeEntitize(cell.InnerText).Trim();
            var digits = NormalizeEan(value);
            if (string.IsNullOrWhiteSpace(digits) || digits.Length < 8)
            {
                return null;
            }

            return digits;
        }

        return null;
    }

    private static bool IsBarcodeHeader(string headerText)
    {
        if (string.IsNullOrWhiteSpace(headerText))
        {
            return false;
        }

        var normalized = RemoveDiacritics(headerText.ToLowerInvariant());
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch))
            {
                sb.Append(ch);
            }
            else
            {
                sb.Append(' ');
            }
        }

        var cleaned = string.Join(
                ' ',
                sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            )
            .Trim();

        if (cleaned.Equals("ean", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Examples on Linea Estetica:
        // - "Código de barras"
        // - "Codigo de barras"
        // - "Código de barras principal"
        return cleaned.Contains("codigo", StringComparison.OrdinalIgnoreCase) &&
               cleaned.Contains("barra", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractCompetitorSku(HtmlDocument doc)
    {
        var node = doc.DocumentNode.SelectSingleNode("//*[contains(@class,'sku_wrapper')]")
                   ?? doc.DocumentNode.SelectSingleNode("//strong[normalize-space()='SKU:']/parent::*");

        if (node is null)
        {
            return null;
        }

        var text = HtmlEntity.DeEntitize(node.InnerText);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var cleaned = text.Replace("SKU:", string.Empty, StringComparison.OrdinalIgnoreCase);
        cleaned = cleaned.Replace("SKU", string.Empty, StringComparison.OrdinalIgnoreCase);
        cleaned = cleaned.Trim();

        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

    private static string? ExtractBrand(HtmlDocument doc)
    {
        var node = doc.DocumentNode.SelectSingleNode("//*[contains(@class,'product_meta')]//a[contains(@href,'/marca/')]")
                   ?? doc.DocumentNode.SelectSingleNode("//a[contains(@href,'/marca/')]");

        if (node is null)
        {
            return null;
        }

        var text = HtmlEntity.DeEntitize(node.InnerText);
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static string? ExtractCategories(HtmlDocument doc)
    {
        var nodes = doc.DocumentNode.SelectNodes("//*[contains(@class,'product_meta')]//a[contains(@href,'/producto-categoria/')]")
                    ?? doc.DocumentNode.SelectNodes("//a[contains(@href,'/producto-categoria/')]");

        if (nodes is null)
        {
            return null;
        }

        var values = nodes
            .Select(n => HtmlEntity.DeEntitize(n.InnerText).Trim())
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return values.Count == 0 ? null : string.Join(", ", values);
    }

    private static PriceValues ExtractPrices(HtmlDocument doc)
    {
        var priceNode = doc.DocumentNode.SelectSingleNode("//p[contains(@class,'price')]")
                        ?? doc.DocumentNode.SelectSingleNode("//*[contains(@class,'price')]//span[contains(@class,'amount')]")?.ParentNode;

        if (priceNode is null)
        {
            return new PriceValues(null, null);
        }

        var promoNode = priceNode.SelectSingleNode(".//ins//span[contains(@class,'amount')]");
        var listNode = priceNode.SelectSingleNode(".//del//span[contains(@class,'amount')]");

        var promo = promoNode is null ? null : ParseMoney(promoNode.InnerText);
        var list = listNode is null ? null : ParseMoney(listNode.InnerText);

        if (promo is null && list is null)
        {
            var amount = priceNode.SelectSingleNode(".//span[contains(@class,'amount')]");
            if (amount is not null)
            {
                var value = ParseMoney(amount.InnerText);
                return new PriceValues(value, value);
            }

            return new PriceValues(null, null);
        }

        if (list is null)
        {
            list = promo;
        }

        if (promo is null)
        {
            promo = list;
        }

        return new PriceValues(list, promo);
    }

    private static string? NormalizeEan(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var matches = EanRegex.Matches(value);
        if (matches.Count == 0)
        {
            return null;
        }

        // Prefer a clean 13-digit EAN if present.
        var best = matches
            .Select(m => m.Value)
            .OrderByDescending(s => s.Length == 13)
            .ThenByDescending(s => s.Length)
            .First();

        if (best.Length == 13)
        {
            return best;
        }

        // UPC-A (12) -> EAN-13 by leading 0
        if (best.Length == 12)
        {
            return "0" + best;
        }

        // GTIN-14 -> take last 13 digits (drop packaging indicator).
        if (best.Length == 14)
        {
            return best.Substring(1);
        }

        return null;
    }

    private sealed class Counters
    {
        public int Processed { get; set; }
        public int Created { get; set; }
        public int Updated { get; set; }
        public int Errors { get; set; }
    }

    private sealed record CatalogItem(
        string Url,
        string? Name,
        string? Description,
        string? Ean,
        string? CompetitorSku,
        string? Brand,
        string? Categories,
        decimal? ListPrice,
        decimal? PromoPrice
    );

    private sealed record PriceValues(decimal? ListPrice, decimal? PromoPrice);

    private sealed record CatalogIndexItem(CatalogItem Item, TokenSet Tokens);

    private sealed record TokenSet(HashSet<string> Tokens, HashSet<string> Numeric);

    private static readonly Regex EanRegex = new(@"\d{12,14}", RegexOptions.Compiled);

    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "x",
        "ml",
        "gr",
        "g",
        "kg",
        "mg",
        "oz",
        "spf",
        "fps",
        "uv",
        "u",
        "nv",
        "vl",
        "cap",
        "caps",
        "tab",
        "tabs",
        "de",
        "del",
        "la",
        "el",
        "los",
        "las",
        "para",
        "con",
        "sin",
        "y",
        "en"
    };
}
