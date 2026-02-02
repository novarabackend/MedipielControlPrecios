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

        foreach (var competitor in competitors)
        {
            if (string.IsNullOrWhiteSpace(competitor.AdapterId))
            {
                summary.Skipped += 1;
                summary.Messages.Add($"Competidor {competitor.Name} sin AdapterId.");
                continue;
            }

            var adapter = _registry.Get(competitor.AdapterId);
            if (adapter is null)
            {
                summary.Errors += 1;
                summary.Messages.Add($"Adapter no encontrado: {competitor.AdapterId}.");
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
                var result = await adapter.RunAsync(context, ct);
                summary.Processed += result.Processed;
                summary.Created += result.Created;
                summary.Updated += result.Updated;
                summary.Errors += result.Errors;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ejecutando adapter {Adapter}.", competitor.AdapterId);
                summary.Errors += 1;
                summary.Messages.Add($"Error ejecutando {competitor.Name}: {ex.Message}");
            }
        }

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
