namespace Instancium.Publisher.State;

using Instancium.Publisher.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

/// <summary>
/// Manages persistent release pipeline state scoped to the target repository working directory.
/// </summary>
public sealed class PipelineStateManager : IPipelineStateManager
{
    private const string StateFileName = ".instancium_pipeline_state.json";

    private readonly ProjectContext _projectContext;
    private readonly ILogger<PipelineStateManager> _logger;

    public PipelineStateManager(ProjectContext projectContext, ILogger<PipelineStateManager> logger)
    {
        _projectContext = projectContext ?? throw new ArgumentNullException(nameof(projectContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private string ResolveStateFilePath() => 
        Path.Combine(_projectContext.WorkingDirectory, StateFileName);

    public async Task SaveAsync(PipelineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string path = ResolveStateFilePath();
        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        string json = JsonSerializer.Serialize(context, options);
        await File.WriteAllTextAsync(path, json);
        _logger.LogInformation("Pipeline state checkpoint saved at step {Step} to {Path}", context.CurrentStep, path);
    }

    public async Task<PipelineContext?> LoadAsync()
    {
        string path = ResolveStateFilePath();
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            string json = await File.ReadAllTextAsync(path);
            var state = JsonSerializer.Deserialize<PipelineContext>(json);
            _logger.LogInformation("Loaded active pipeline state at step {Step} from {Path}", state?.CurrentStep, path);
            return state;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Corrupted pipeline state file at {Path}", path);
            return null;
        }
    }

    public Task RollbackAsync()
    {
        string path = ResolveStateFilePath();
        if (File.Exists(path))
        {
            File.Delete(path);
            _logger.LogInformation("Pipeline state file deleted: {Path}", path);
        }
        return Task.CompletedTask;
    }
}