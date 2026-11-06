namespace Instancium.Publisher.Services.Runtime;

using Instancium.Publisher.Configuration;
using Instancium.Publisher.Core.Domain;
using Instancium.Publisher.Core.Interfaces;
using Microsoft.Extensions.Options;
using System.Reflection;


internal class EnvironmentInfoProvider(IOptions<PublisherSettings> options) : IEnvironmentInfoProvider
{
    public string GetApplicationVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            int plusIndex = informational.IndexOf('+');
            return plusIndex >= 0 ? informational[..plusIndex] : informational;
        }
        var nameVersion = assembly.GetName().Version;
        return nameVersion != null
            ? $"{nameVersion.Major}.{nameVersion.Minor}.{nameVersion.Build}"
            : "0.0.0";
    }

    public async Task<string> GetActiveModelIdAsync(CancellationToken ct = default)
    {
        // Placeholder for actual implementation to retrieve the active AI model ID.
        var modelIdFromSettings = options.Value.AiModelId;
        await Task.Delay(10, ct); // Simulate async work
        return modelIdFromSettings; // Example model ID
    }

    public async Task<RuntimeEnvironmentInfo> GetEnvironmentInfoAsync(CancellationToken ct = default)
    {
        var version = GetApplicationVersion();
        var modelId = await GetActiveModelIdAsync(ct);
        return new RuntimeEnvironmentInfo(version, modelId);
    }
}

