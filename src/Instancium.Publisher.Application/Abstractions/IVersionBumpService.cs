using Instancium.Publisher.Domain.Versioning;

namespace Instancium.Publisher.Application.Abstractions;

/// <summary>Catalog facade over all registered bump strategies (built-in and future plugins).</summary>
public interface IVersionBumpService
{
    IReadOnlyList<IVersionBumpStrategy> Strategies { get; }
    SemanticVersion CalculateNext(SemanticVersion? current, string strategyId, VersionBumpContext? context = null);
}
