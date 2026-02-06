using System.Diagnostics;
using Medipiel.Api.Data;
using Medipiel.Api.Models;
using Medipiel.Competitors.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Medipiel.Api.Services;

public sealed class CompetitorRunService
{
    private readonly AppDbContext _db;
    private readonly CompetitorAdapterRegistry _registry;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CompetitorRunService> _logger;

    public CompetitorRunService(
        AppDbContext db,
        CompetitorAdapterRegistry registry,
        IConfiguration configuration,
        ILogger<CompetitorRunService> logger)
    {
        _db = db;
        _registry = registry;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<RunnerSummary> RunAsync(
        int runId,
        int? competitorId,
        bool onlyNew,
        int batchSize,
        CancellationToken ct)
    {
        var query = _db.Competitors.AsNoTracking().Where(x => x.IsActive);
        if (competitorId.HasValue)
        {
            query = query.Where(x => x.Id == competitorId.Value);
        }

        var competitors = await query.ToListAsync(ct);
        var connectionName = _configuration.GetValue<string>("Adapters:ConnectionName") ?? "Default";
        var runDate = DateTime.Now;

        var summary = new RunnerSummary { RunId = runId };

        _logger.LogInformation(
            "Scheduler run {RunId} started. Competitors={Count} OnlyNew={OnlyNew} BatchSize={BatchSize}.",
            runId,
            competitors.Count,
            onlyNew,
            batchSize
        );

        foreach (var competitor in competitors)
        {
            if (string.IsNullOrWhiteSpace(competitor.AdapterId))
            {
                summary.Skipped += 1;
                summary.Messages.Add($"Competidor {competitor.Name} sin AdapterId.");
                _logger.LogWarning("Run {RunId}: competidor {Name} sin AdapterId.", runId, competitor.Name);
                continue;
            }

            var adapter = _registry.Get(competitor.AdapterId);
            if (adapter is null)
            {
                summary.Errors += 1;
                summary.Messages.Add($"Adapter no encontrado: {competitor.AdapterId}.");
                _logger.LogWarning("Run {RunId}: adapter no encontrado {AdapterId}.", runId, competitor.AdapterId);
                continue;
            }

            var context = new AdapterContext(
                connectionName,
                competitor.Id,
                competitor.BaseUrl ?? string.Empty,
                summary.RunId,
                runDate,
                onlyNew,
                batchSize
            );

            try
            {
                _logger.LogInformation(
                    "Run {RunId}: iniciando {Competitor} ({AdapterId}).",
                    runId,
                    competitor.Name,
                    competitor.AdapterId
                );

                var stopwatch = Stopwatch.StartNew();
                var result = await adapter.RunAsync(context, ct);
                stopwatch.Stop();

                summary.Processed += result.Processed;
                summary.Created += result.Created;
                summary.Updated += result.Updated;
                summary.Errors += result.Errors;

                var alertsCreated = await GenerateAlertsAsync(competitor.Id, runDate, ct);
                if (alertsCreated > 0)
                {
                    _logger.LogInformation(
                        "Run {RunId}: alertas generadas para {Competitor} = {Count}.",
                        runId,
                        competitor.Name,
                        alertsCreated
                    );
                }

                _logger.LogInformation(
                    "Run {RunId}: finalizo {Competitor} ({AdapterId}) en {ElapsedMs}ms. Processed={Processed} Updated={Updated} Errors={Errors}.",
                    runId,
                    competitor.Name,
                    competitor.AdapterId,
                    stopwatch.ElapsedMilliseconds,
                    result.Processed,
                    result.Updated,
                    result.Errors
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ejecutando adapter {Adapter}.", competitor.AdapterId);
                summary.Errors += 1;
                summary.Messages.Add($"Error ejecutando {competitor.Name}: {ex.Message}");
            }
        }

        _logger.LogInformation(
            "Scheduler run {RunId} finished. Processed={Processed} Updated={Updated} Errors={Errors} Skipped={Skipped}.",
            runId,
            summary.Processed,
            summary.Updated,
            summary.Errors,
            summary.Skipped
        );

        return summary;
    }

    private async Task<int> GenerateAlertsAsync(int competitorId, DateTime runDate, CancellationToken ct)
    {
        var snapshotDate = DateOnly.FromDateTime(runDate);
        var dayStart = runDate.Date;
        var dayEnd = dayStart.AddDays(1);

        var rules = await _db.AlertRules.AsNoTracking()
            .Where(x => x.Active)
            .ToListAsync(ct);

        if (rules.Count == 0)
        {
            return 0;
        }

        var ruleByBrandId = rules.ToDictionary(x => x.BrandId);

        var snapshots = await _db.PriceSnapshots.AsNoTracking()
            .Where(x => x.CompetitorId == competitorId && x.SnapshotDate == snapshotDate)
            .Include(x => x.Product)
            .ToListAsync(ct);

        if (snapshots.Count == 0)
        {
            return 0;
        }

        var existing = await _db.Alerts.AsNoTracking()
            .Where(x => x.CompetitorId == competitorId && x.CreatedAt >= dayStart && x.CreatedAt < dayEnd)
            .Select(x => new { x.ProductId, x.Type })
            .ToListAsync(ct);

        var existingSet = new HashSet<string>(existing.Select(x => $"{x.ProductId}:{x.Type}"));
        var newAlerts = new List<Alert>();

        foreach (var snapshot in snapshots)
        {
            var product = snapshot.Product;
            if (product.BrandId is null)
            {
                continue;
            }

            if (!ruleByBrandId.TryGetValue(product.BrandId.Value, out var rule))
            {
                continue;
            }

            if (rule.ListPriceThresholdPercent.HasValue &&
                snapshot.ListPrice.HasValue &&
                product.MedipielListPrice.HasValue &&
                product.MedipielListPrice.Value > 0)
            {
                var delta = ((snapshot.ListPrice.Value - product.MedipielListPrice.Value) / product.MedipielListPrice.Value) * 100m;
                if (Math.Abs(delta) >= rule.ListPriceThresholdPercent.Value)
                {
                    var key = $"{product.Id}:list";
                    if (!existingSet.Contains(key))
                    {
                        existingSet.Add(key);
                        newAlerts.Add(new Alert
                        {
                            ProductId = product.Id,
                            CompetitorId = competitorId,
                            AlertRuleId = rule.Id,
                            Type = "list",
                            Message = $"Brecha lista {delta:+0.##;-0.##}% vs Medipiel."
                        });
                    }
                }
            }

            if (rule.PromoPriceThresholdPercent.HasValue &&
                snapshot.PromoPrice.HasValue &&
                product.MedipielPromoPrice.HasValue &&
                product.MedipielPromoPrice.Value > 0)
            {
                var delta = ((snapshot.PromoPrice.Value - product.MedipielPromoPrice.Value) / product.MedipielPromoPrice.Value) * 100m;
                if (Math.Abs(delta) >= rule.PromoPriceThresholdPercent.Value)
                {
                    var key = $"{product.Id}:promo";
                    if (!existingSet.Contains(key))
                    {
                        existingSet.Add(key);
                        newAlerts.Add(new Alert
                        {
                            ProductId = product.Id,
                            CompetitorId = competitorId,
                            AlertRuleId = rule.Id,
                            Type = "promo",
                            Message = $"Brecha promo {delta:+0.##;-0.##}% vs Medipiel."
                        });
                    }
                }
            }
        }

        var missingMatches = await _db.CompetitorProducts.AsNoTracking()
            .Where(x => x.CompetitorId == competitorId && x.MatchMethod == "no_match")
            .Select(x => x.ProductId)
            .ToListAsync(ct);

        foreach (var productId in missingMatches)
        {
            var key = $"{productId}:no_match";
            if (existingSet.Contains(key))
            {
                continue;
            }

            existingSet.Add(key);
            newAlerts.Add(new Alert
            {
                ProductId = productId,
                CompetitorId = competitorId,
                AlertRuleId = null,
                Type = "no_match",
                Message = "Producto sin match en el competidor."
            });
        }

        if (newAlerts.Count == 0)
        {
            return 0;
        }

        _db.Alerts.AddRange(newAlerts);
        await _db.SaveChangesAsync(ct);
        return newAlerts.Count;
    }
}

public sealed class RunnerSummary
{
    public int RunId { get; set; }
    public int Processed { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Errors { get; set; }
    public int Skipped { get; set; }
    public List<string> Messages { get; } = new();
}
