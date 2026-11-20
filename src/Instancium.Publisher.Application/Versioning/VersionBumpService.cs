using Instancium.Publisher.Application.Abstractions;
using Instancium.Publisher.Domain.Versioning;

namespace Instancium.Publisher.Application.Versioning;

/// <summary>Catalog and dispatcher over registered <see cref="IVersionBumpStrategy"/> instances.</summary>
public sealed class VersionBumpService : IVersionBumpService
{
    private readonly Dictionary<string, IVersionBumpStrategy> _byId;

    public VersionBumpService(IEnumerable<IVersionBumpStrategy> strategies)
    {
        ArgumentNullException.ThrowIfNull(strategies);
        var list = strategies.ToList();
        Strategies = list;
        _byId = new Dictionary<string, IVersionBumpStrategy>(StringComparer.Ordinal);
        foreach (IVersionBumpStrategy strategy in list)
        {
            _byId[strategy.Id] = strategy;
        }
    }

    public IReadOnlyList<IVersionBumpStrategy> Strategies { get; }

    public SemanticVersion CalculateNext(
        SemanticVersion? current,
        string strategyId,
        VersionBumpContext? context = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyId);
        if (!_byId.TryGetValue(strategyId, out IVersionBumpStrategy? strategy))
        {
            string known = string.Join(", ", _byId.Keys);
            throw new ArgumentOutOfRangeException(
                nameof(strategyId),
                strategyId,
                $"Unknown version bump strategy '{strategyId}'. Known strategies: {known}");
        }

        return strategy.CalculateNext(current, context ?? new VersionBumpContext());
    }
}
