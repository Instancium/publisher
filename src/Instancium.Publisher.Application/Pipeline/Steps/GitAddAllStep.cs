using Instancium.Publisher.Application.Abstractions;

namespace Instancium.Publisher.Application.Pipeline.Steps;

public class GitAddAllStep
{
    private readonly IGitService _gitService;

    public GitAddAllStep(IGitService gitService) => _gitService = gitService;

    public Task ExecuteAsync(CancellationToken ct = default)
    {
        return _gitService.AddAllAsync(ct);
    }
}