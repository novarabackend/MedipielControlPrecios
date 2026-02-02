namespace Medipiel.Competitors.Abstractions;

public interface ICompetitorAdapter
{
    string AdapterId { get; }
    string Name { get; }

    Task<AdapterRunResult> RunAsync(AdapterContext context, CancellationToken ct);
}

public sealed record AdapterContext(
    string ConnectionName,
    int CompetitorId,
    string BaseUrl,
    int RunId,
    DateTime RunDate,
    bool OnlyNew,
    int BatchSize
);

public sealed record AdapterRunResult(
    int Processed,
    int Created,
    int Updated,
    int Errors,
    string? Message
);
