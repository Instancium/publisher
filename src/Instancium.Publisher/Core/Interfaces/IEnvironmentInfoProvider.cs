namespace Instancium.Publisher.Core.Interfaces;

using System.Threading;
using System.Threading.Tasks;
using Instancium.Publisher.Core.Domain;

/// <summary>
/// Provides read-only runtime diagnostics and environment metadata.
/// </summary>
public interface IEnvironmentInfoProvider
{
    /// <summary>
    /// Gets the compiled assembly semantic version.
    /// </summary>
    string GetApplicationVersion();

    /// <summary>
    /// Retrieves the resolved active AI model identifier from the gateway or runtime settings.
    /// </summary>
    Task<string> GetActiveModelIdAsync(CancellationToken ct = default);

    /// <summary>
    /// Retrieves the consolidated runtime environment snapshot.
    /// </summary>
    Task<RuntimeEnvironmentInfo> GetEnvironmentInfoAsync(CancellationToken ct = default);
}