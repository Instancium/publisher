namespace Instancium.Publisher.Tests.TestInfrastructure;

using System.Collections.Generic;
using System.Threading.Tasks;
using Instancium.Publisher.Core.Interfaces;
using Instancium.Publisher.State;

/// <summary>
/// In-memory test double for IPipelineStateManager capturing every saved snapshot,
/// so tests can assert journaling behavior (save-before-mutation) of pipeline steps.
/// SemanticVersion is immutable, therefore a property-wise copy is a safe snapshot.
/// </summary>
public sealed class InMemoryPipelineStateManager : IPipelineStateManager
{
    private readonly List<PipelineContext> _savedContexts = new();
    private PipelineContext? _loadedContext;
    private int _rollbackCount;

    public IReadOnlyList<PipelineContext> SavedContexts => _savedContexts;

    public int SaveCount => _savedContexts.Count;

    public int RollbackCount => _rollbackCount;

    public void SetLoadedContext(PipelineContext? context) => _loadedContext = context;

    public Task SaveAsync(PipelineContext context)
    {
        _savedContexts.Add(new PipelineContext
        {
            CurrentStep = context.CurrentStep,
            SourceBranch = context.SourceBranch,
            ReleaseBranch = context.ReleaseBranch,
            CurrentVersion = context.CurrentVersion,
            NewVersion = context.NewVersion,
            CommitMessage = context.CommitMessage,
            ReleaseNotes = context.ReleaseNotes
        });
        return Task.CompletedTask;
    }

    public Task<PipelineContext?> LoadAsync() => Task.FromResult(_loadedContext);

    public Task RollbackAsync()
    {
        _rollbackCount++;
        _loadedContext = null;
        return Task.CompletedTask;
    }
}
