namespace Instancium.Publisher.State;

using Instancium.Publisher.Core.Interfaces;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

public sealed class PipelineStateManager : IPipelineStateManager
{
    private const string StateFileName = "Instancium.Publisher_Pipeline_State.json";
    private readonly string _stateFilePath;

    public PipelineStateManager()
    {
        // State file is saved in the working directory of the repository being published
        _stateFilePath = Path.Combine(Environment.CurrentDirectory, StateFileName);
    }

    public async Task SaveAsync(PipelineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        string json = JsonSerializer.Serialize(context, options);
        await File.WriteAllTextAsync(_stateFilePath, json);
    }

    public async Task<PipelineContext?> LoadAsync()
    {
        if (!File.Exists(_stateFilePath))
        {
            return null;
        }

        try
        {
            string json = await File.ReadAllTextAsync(_stateFilePath);
            return JsonSerializer.Deserialize<PipelineContext>(json);
        }
        catch (JsonException)
        {
            // If the state file is corrupted, treat it as non-existent to force a fresh start
            return null;
        }
    }

    public Task RollbackAsync()
    {
        if (File.Exists(_stateFilePath))
        {
            File.Delete(_stateFilePath);
        }

        return Task.CompletedTask;
    }
}