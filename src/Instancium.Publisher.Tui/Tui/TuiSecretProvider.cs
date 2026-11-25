namespace Instancium.Publisher.Tui;

using Instancium.Publisher.Application.Abstractions;
using Instancium.Publisher.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Spectre.Console;

/// <summary>
/// Resolves the release key container password from the configured environment variable first
/// (supporting non-interactive runs) and falls back to a masked interactive terminal prompt.
/// </summary>
public sealed class TuiSecretProvider : ISecretProvider
{
    private readonly SigningSettings _settings;

    public TuiSecretProvider(IOptions<PublisherSettings> settings)
    {
        _settings = settings?.Value.Signing ?? throw new ArgumentNullException(nameof(settings));
    }

    public Task<string> GetReleaseKeyPasswordAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (!string.IsNullOrWhiteSpace(_settings.PasswordEnvironmentVariable))
        {
            string? fromEnv = Environment.GetEnvironmentVariable(_settings.PasswordEnvironmentVariable);
            if (!string.IsNullOrEmpty(fromEnv))
            {
                return Task.FromResult(fromEnv);
            }
        }

        string password = AnsiConsole.Prompt(
            new TextPrompt<string>("Enter Release Key container password:")
                .Secret());

        return Task.FromResult(password);
    }
}
