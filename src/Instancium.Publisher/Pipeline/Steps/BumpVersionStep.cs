using Instancium.Publisher.Core.Domain;
using Instancium.Publisher.Core.Interfaces;
using Instancium.Publisher.State;

namespace Instancium.Publisher.Pipeline.Steps;

public class BumpVersionStep
{
    private readonly IVersionEngine _engine;

    public BumpVersionStep(IVersionEngine engine) => _engine = engine;

    public async Task ExecuteAsync(PipelineContext context, string projectPath, VersionBumpStrategy strategy, CancellationToken ct = default)
    {
        context.CurrentVersion = await _engine.ReadVersionFromProjectAsync(projectPath, ct);
        context.NewVersion = _engine.CalculateNext(context.CurrentVersion, strategy);

        await _engine.WriteVersionToProjectAsync(projectPath, context.NewVersion, ct);
    }
}