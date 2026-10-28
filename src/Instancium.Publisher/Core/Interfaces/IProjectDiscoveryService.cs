namespace Instancium.Publisher.Core.Interfaces;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Discovers SDK-style project files that participate in repository-wide versioning.
/// </summary>
public interface IProjectDiscoveryService
{
    /// <summary>
    /// Returns absolute paths of all *.csproj files under <paramref name="rootDirectory"/>,
    /// excluding build outputs and tooling directories, in deterministic order.
    /// </summary>
    Task<IReadOnlyList<string>> DiscoverProjectFilesAsync(string rootDirectory, CancellationToken ct = default);
}
