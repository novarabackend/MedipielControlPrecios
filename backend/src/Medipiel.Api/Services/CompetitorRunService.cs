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
