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
        var delayMs = GetDelayMs("Adapters:Farmatodo:DelayMs", 300);
        var storeGroupId = Configuration.GetValue<int?>("Adapters:Farmatodo:StoreGroupId") ?? 26;
        var apiBase = Configuration.GetValue<string>("Adapters:Farmatodo:ApiBase")
            ?? "https://stunning-base-164402.appspot.com/_ah/api";

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

        Logger.LogInformation(
            "Farmatodo: inicio {Total} productos (OnlyNew={OnlyNew}, BatchSize={BatchSize}).",
            total,
            context.OnlyNew,
            context.BatchSize
        );

        foreach (var product in products)
        {
            ct.ThrowIfCancellationRequested();
            counters.Processed += 1;

            if (string.IsNullOrWhiteSpace(product.Ean))
            {
                counters.Errors += 1;
                continue;
            }

            var apiUrl = BuildApiUrl(apiBase, product.Ean!, storeGroupId);
            var json = await GetHtmlAsync(apiUrl, delayMs, ct);
            if (string.IsNullOrWhiteSpace(json))
            {
                counters.Errors += 1;
                continue;
            }

            AlgoliaItem? item;
            try
            {
                item = JsonSerializer.Deserialize<AlgoliaItem>(json, JsonOptions);
            }
            catch (JsonException ex)
            {
                Logger.LogWarning(ex, "Farmatodo: respuesta invalida para EAN {Ean}", product.Ean);
                counters.Errors += 1;
                continue;
            }

            if (item is null || string.IsNullOrWhiteSpace(item.ItemUrl))
            {
                await db.MarkNoMatchAsync(product.Id, context.CompetitorId, ct);
                noMatchCount += 1;
                continue;
            }

            var url = BuildProductUrl(context.BaseUrl, item.ItemUrl);
            if (string.IsNullOrWhiteSpace(url))
            {
                counters.Errors += 1;
                continue;
            }

            var prices = ResolvePrices(item);

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

    private static string BuildApiUrl(string apiBase, string ean, int storeGroupId)
    {
        var baseUrl = apiBase.TrimEnd('/');
        return $"{baseUrl}/algolia/getItemByBarcode?barcode={Uri.EscapeDataString(ean)}&idStoreGroup={storeGroupId}";
    }

    private static string? BuildProductUrl(string baseUrl, string itemUrl)
    {
        if (string.IsNullOrWhiteSpace(itemUrl))
        {
            return null;
        }

        var normalized = itemUrl.Trim('/');
        return Combine(baseUrl.TrimEnd('/'), $"producto/{normalized}");
    }

    private static PriceValues ResolvePrices(AlgoliaItem item)
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

    private sealed class Counters
    {
        public int Processed { get; set; }
        public int Created { get; set; }
        public int Updated { get; set; }
        public int Errors { get; set; }
    }

    private sealed record AlgoliaItem(
        [property: JsonPropertyName("itemUrl")] string? ItemUrl,
        [property: JsonPropertyName("fullPrice")] decimal? FullPrice,
        [property: JsonPropertyName("offerPrice")] decimal? OfferPrice,
        [property: JsonPropertyName("barcode")] string? Barcode,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("marca")] string? Marca
    );

    private sealed record PriceValues(decimal? ListPrice, decimal? PromoPrice);
}
