using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Medipiel.Competitors.Abstractions;
using Medipiel.Competitors.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Medipiel.Competitors.BellaPiel;

public sealed class BellaPielAdapter : CompetitorAdapterBase
{
    public BellaPielAdapter(IConfiguration configuration, ILogger<BellaPielAdapter> logger)
        : base(configuration, logger)
    {
    }

    public override string AdapterId => "bellapiel";
    public override string Name => "Bella Piel";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public override async Task<AdapterRunResult> RunAsync(AdapterContext context, CancellationToken ct)
    {
        var connectionString = GetConnectionString(context.ConnectionName);
        var db = CreateDb(connectionString);
        var delayMs = GetDelayMs("Adapters:BellaPiel:DelayMs", 300);
        var minScore = Configuration.GetValue<double?>("Adapters:BellaPiel:MinScore") ?? 0.55;
        var useAi = Configuration.GetValue<bool?>("Adapters:BellaPiel:UseAi") ?? true;
        var aiMinConfidence = Configuration.GetValue<double?>("Adapters:BellaPiel:AiMinConfidence") ?? 0.6;
        var aiCandidates = Configuration.GetValue<int?>("Adapters:BellaPiel:AiCandidates") ?? 5;

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
            "BellaPiel: inicio {Total} productos (OnlyNew={OnlyNew}, BatchSize={BatchSize}).",
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
                    if (await TrySyncByUrlAsync(db, context, product, delayMs, ct))
                    {
                        counters.Updated += 1;
                    }
                    else
                    {
                        counters.Errors += 1;
                    }
                    continue;
                }

                var outcome = await TrySyncBySearchAsync(db, context, product, delayMs, minScore, useAi, aiMinConfidence, aiCandidates, ct);
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
                Logger.LogWarning(ex, "BellaPiel: error procesando producto {ProductId}", product.Id);
                counters.Errors += 1;
            }

            if (counters.Processed % logEvery == 0 || counters.Processed == total)
            {
                Logger.LogInformation(
                    "BellaPiel: progreso {Processed}/{Total} (Updated={Updated}, Errors={Errors}, NoMatch={NoMatch}).",
                    counters.Processed,
                    total,
                    counters.Updated,
                    counters.Errors,
                    noMatchCount
                );
            }
        }

        Logger.LogInformation(
            "BellaPiel: fin Processed={Processed} Updated={Updated} Errors={Errors} NoMatch={NoMatch}.",
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
        int delayMs,
        CancellationToken ct)
    {
        var baseUrl = context.BaseUrl.TrimEnd('/');
        var slug = ExtractSlugFromUrl(product.Url!);
        if (string.IsNullOrWhiteSpace(slug))
        {
            Logger.LogWarning("BellaPiel: no se pudo obtener slug de {Url}", product.Url);
            return false;
        }

        var apiUrl = $"{baseUrl}/api/catalog_system/pub/products/search/{Uri.EscapeDataString(slug)}/p";
        var json = await GetHtmlAsync(apiUrl, delayMs, ct);
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        var item = ParseProducts(json).FirstOrDefault();
        if (item is null)
        {
            Logger.LogWarning("BellaPiel: respuesta vacia para {Slug}", slug);
            return false;
        }

        var url = BuildProductUrl(baseUrl, item);
        var prices = ResolvePrices(item);

        await db.UpsertCompetitorProductAsync(
            product.Id,
            context.CompetitorId,
            url ?? product.Url!,
            "url",
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

        return true;
    }

    private async Task<MatchOutcome> TrySyncBySearchAsync(
        CompetitorDb db,
        AdapterContext context,
        ProductRow product,
        int delayMs,
        double minScore,
        bool useAi,
        double aiMinConfidence,
        int aiCandidates,
        CancellationToken ct)
    {
        var query = BuildSearchQuery(product.Description);
        if (string.IsNullOrWhiteSpace(query))
        {
            return MatchOutcome.NoMatch;
        }

        var baseUrl = context.BaseUrl.TrimEnd('/');
        var apiUrl = $"{baseUrl}/api/catalog_system/pub/products/search/{Uri.EscapeDataString(query)}";
        var json = await GetHtmlAsync(apiUrl, delayMs, ct);
        if (string.IsNullOrWhiteSpace(json))
        {
            return MatchOutcome.Error;
        }

        var candidates = ParseProducts(json);
        if (candidates.Count == 0)
        {
            return MatchOutcome.NoMatch;
        }

        var ranked = RankCandidates(product.Description, candidates);
        var best = ranked.FirstOrDefault();
        if (best?.Product is null)
        {
            return MatchOutcome.Error;
        }

        if (best.Score < minScore && useAi)
        {
            var selection = await TrySelectWithAiAsync(product.Description, ranked, aiCandidates, ct);
            if (selection is not null && selection.Confidence >= aiMinConfidence)
            {
                var aiUrl = BuildProductUrl(baseUrl, selection.Product);
                if (string.IsNullOrWhiteSpace(aiUrl))
                {
                    return MatchOutcome.Error;
                }

                var aiPrices = ResolvePrices(selection.Product);
                await db.UpsertCompetitorProductAsync(
                    product.Id,
                    context.CompetitorId,
                    aiUrl,
                    "ai",
                    (decimal)selection.Confidence,
                    DateTime.UtcNow,
                    ct
                );

                await db.UpsertPriceSnapshotAsync(
                    product.Id,
                    context.CompetitorId,
                    context.RunDate.Date,
                    aiPrices.ListPrice,
                    aiPrices.PromoPrice,
                    ct
                );

                return MatchOutcome.Matched;
            }
        }

        if (best.Score < minScore)
        {
            Logger.LogInformation(
                "BellaPiel: sin match para {ProductId} score {Score:0.00}",
                product.Id,
                best.Score
            );
            return MatchOutcome.NoMatch;
        }

        var url = BuildProductUrl(baseUrl, best.Product);
        if (string.IsNullOrWhiteSpace(url))
        {
            return MatchOutcome.Error;
        }

        var prices = ResolvePrices(best.Product);
        await db.UpsertCompetitorProductAsync(
            product.Id,
            context.CompetitorId,
            url,
            "name",
            (decimal)best.Score,
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

        return MatchOutcome.Matched;
    }

    private enum MatchOutcome
    {
        Matched,
        NoMatch,
        Error
    }

    private static string? ExtractSlugFromUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var segments = uri.AbsolutePath
            .Trim('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0)
        {
            return null;
        }

        if (segments[^1].Equals("p", StringComparison.OrdinalIgnoreCase) && segments.Length >= 2)
        {
            return segments[^2];
        }

        return segments[^1];
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

    private static List<VtexProduct> ParseProducts(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<VtexProduct>>(json, JsonOptions) ?? new List<VtexProduct>();
        }
        catch (JsonException)
        {
            return new List<VtexProduct>();
        }
    }

    private static string? BuildProductUrl(string baseUrl, VtexProduct product)
    {
        if (!string.IsNullOrWhiteSpace(product.Link))
        {
            return product.Link;
        }

        if (!string.IsNullOrWhiteSpace(product.LinkText))
        {
            return Combine(baseUrl, $"{product.LinkText}/p");
        }

        return null;
    }

    private static PriceValues ResolvePrices(VtexProduct product)
    {
        var offer = product.Items?.FirstOrDefault()?.Sellers?.FirstOrDefault()?.CommertialOffer;
        if (offer is null)
        {
            return new PriceValues(null, null);
        }

        var list = offer.ListPrice ?? offer.PriceWithoutDiscount ?? offer.Price;
        var promo = offer.Price ?? list;
        if (list is null)
        {
            list = promo;
        }

        return new PriceValues(list, promo);
    }

    private static List<CandidateResult> RankCandidates(string description, List<VtexProduct> candidates)
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

    private static string BuildCandidateText(VtexProduct product)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(product.Brand))
        {
            parts.Add(product.Brand);
        }
        if (!string.IsNullOrWhiteSpace(product.ProductName))
        {
            parts.Add(product.ProductName);
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

    private async Task<AiSelection?> TrySelectWithAiAsync(
        string description,
        List<CandidateResult> ranked,
        int maxCandidates,
        CancellationToken ct)
    {
        var apiKey = ResolveOpenAiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Logger.LogWarning("BellaPiel: OpenAI API key no configurada.");
            return null;
        }

        var candidates = ranked
            .Where(r => r.Product is not null)
            .Take(Math.Max(1, maxCandidates))
            .ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        var model = Configuration.GetValue<string>("Adapters:BellaPiel:AiModel") ?? "gpt-4o-mini";
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
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await http.PostAsync("https://api.openai.com/v1/chat/completions", content, ct);
        if (!response.IsSuccessStatusCode)
        {
            Logger.LogWarning("BellaPiel: OpenAI fallo {Status}", response.StatusCode);
            return null;
        }

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
            Logger.LogWarning(ex, "BellaPiel: respuesta OpenAI invalida.");
            return null;
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
        builder.AppendLine("Producto catálogo:");
        builder.AppendLine(description);
        builder.AppendLine();
        builder.AppendLine("Candidatos:");
        for (var i = 0; i < candidates.Count; i += 1)
        {
            var candidate = candidates[i].Product!;
            var offer = candidate.Items?.FirstOrDefault()?.Sellers?.FirstOrDefault()?.CommertialOffer;
            builder.AppendLine($"[{i}] Marca: {candidate.Brand} | Nombre: {candidate.ProductName} | Ref: {candidate.ProductReference} | Precio: {offer?.Price} | Lista: {offer?.ListPrice}");
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

    private sealed record CandidateResult(VtexProduct? Product, double Score);
    private sealed record AiSelection(VtexProduct Product, double Confidence);

    private sealed class Counters
    {
        public int Processed { get; set; }
        public int Created { get; set; }
        public int Updated { get; set; }
        public int Errors { get; set; }
    }

    private sealed record VtexProduct(
        string? ProductId,
        string? ProductName,
        string? Brand,
        string? ProductReference,
        string? Link,
        string? LinkText,
        List<VtexItem>? Items
    );

    private sealed record VtexItem(
        string? ItemId,
        string? Name,
        string? Ean,
        List<VtexSeller>? Sellers
    );

    private sealed record VtexSeller(
        string? SellerId,
        [property: JsonPropertyName("commertialOffer")] VtexOffer? CommertialOffer
    );

    private sealed record VtexOffer(
        decimal? Price,
        decimal? ListPrice,
        decimal? PriceWithoutDiscount
    );

    private sealed record PriceValues(decimal? ListPrice, decimal? PromoPrice);
}
