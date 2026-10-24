namespace Instancium.Publisher.Pipeline.Steps;

using Instancium.Publisher.Core.Interfaces;
using Instancium.Publisher.State;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

public sealed class GenerateCommitMessageStep
{
    private readonly IGitService _gitService;
    private readonly IAiCommitAssistant _assistant;

    public GenerateCommitMessageStep(IGitService gitService, IAiCommitAssistant assistant)
    {
        _gitService = gitService;
        _assistant = assistant;
    }

    public async IAsyncEnumerable<string> StreamExecuteAsync(
        PipelineContext context,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        string diff = await _gitService.GetCachedDiffAsync(ct);
        string diffStat = await _gitService.GetCachedDiffStatAsync(ct);

        if (string.IsNullOrWhiteSpace(diff))
        {
            yield return "chore: auto-commit updates";
            yield break;
        }

        await foreach (var chunk in _assistant.StreamCommitMessageAsync(diff, diffStat, ct))
        {
            yield return chunk;
        }
    }
}