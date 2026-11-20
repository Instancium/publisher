namespace Instancium.Publisher.Services.Runtime;

using Instancium.Publisher.Configuration;
using Instancium.Publisher.Domain.Runtime;
using Instancium.Publisher.Application.Abstractions;
using InstantAIGate.Client.Telemetry;
using InstantAIGate.Core.Dtos.Status;
using Microsoft.Extensions.Options;
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Provides runtime diagnostics: the compiled assembly version and the real active model
/// identifier queried from the gateway, with a configured-value fallback for display purposes.
/// </summary>
internal sealed class EnvironmentInfoProvider : IEnvironmentInfoProvider
{
    private readonly PublisherSettings _settings;
    private readonly IGatewayTelemetryClient _telemetryClient;

    public EnvironmentInfoProvider(
        IOptions<PublisherSettings> options,
        IGatewayTelemetryClient telemetryClient)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(telemetryClient);
        _settings = options.Value;
        _telemetryClient = telemetryClient;
    }

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
        try
        {
            NativeModelDetails details = await _telemetryClient.GetActiveModelDetailsAsync(ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(details.RepoId))
            {
                return details.RepoId;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Display-only metadata (TUI hero, release footer): an offline or busy gateway
            // must not break commit/release flows. Fall back to the configured model id.
        }
        return _settings.AiModelId;
    }

    public async Task<RuntimeEnvironmentInfo> GetEnvironmentInfoAsync(CancellationToken ct = default)
    {
        string version = GetApplicationVersion();
        string modelId = await GetActiveModelIdAsync(ct).ConfigureAwait(false);
        return new RuntimeEnvironmentInfo(version, modelId);
    }
}