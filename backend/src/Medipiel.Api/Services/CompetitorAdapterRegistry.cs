using Medipiel.Competitors.Abstractions;

namespace Medipiel.Api.Services;

public sealed class CompetitorAdapterRegistry
{
    private readonly Dictionary<string, ICompetitorAdapter> _adapters =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<ICompetitorAdapter> Adapters => _adapters.Values;

    public bool TryAdd(ICompetitorAdapter adapter)
    {
        if (string.IsNullOrWhiteSpace(adapter.AdapterId))
        {
            return false;
        }

        if (_adapters.ContainsKey(adapter.AdapterId))
        {
            return false;
        }

        _adapters[adapter.AdapterId] = adapter;
        return true;
    }

    public ICompetitorAdapter? Get(string adapterId)
    {
        return _adapters.TryGetValue(adapterId, out var adapter) ? adapter : null;
    }
}
