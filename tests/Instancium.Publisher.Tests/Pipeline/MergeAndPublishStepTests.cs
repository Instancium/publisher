namespace Instancium.Publisher.Tests.Pipeline;

using Instancium.Publisher.Application.Abstractions;
using Instancium.Publisher.Application.Pipeline;
using Instancium.Publisher.Application.Pipeline.Steps;
using Instancium.Publisher.Application.State;
using Instancium.Publisher.Domain.Versioning;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

/// <summary>
/// Verifies fast-forward-first merge behavior, fallback forced merge on divergence,
/// and target-branch backporting so release tags stay reachable from the source branch.
/// </summary>
public sealed class MergeAndPublishStepTests
{
    private readonly Mock<IGitService> _gitMock = new();
    private readonly Mock<IProcessExecutionService> _processMock = new();
    private readonly Mock<IProgressReporter> _reporterMock = new();
    private readonly Mock<IReleasePublisher> _publisherMock = new();
    private readonly ProjectContext _projectContext = new() { WorkingDirectory = "/test/repo" };

    private MergeAndPublishStep CreateStep() =>
        new(_gitMock.Object, _processMock.Object, _reporterMock.Object, _publisherMock.Object, _projectContext);

    [Fact]
    public async Task ExecuteAsync_LinearHistory_UsesFastForwardWithoutMergeCommitsAndBackportsTargetBranch()
    {
        _processMock
            .Setup(p => p.ExecuteAsync(
                "git",
                It.IsAny<IReadOnlyList<string>>(),
                _projectContext.WorkingDirectory,
                It.IsAny<Action<string>?>(),
                It.IsAny<Action<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var context = new PipelineContext
        {
            SourceBranch = "dev",
            ReleaseBranch = "release-prep/v1.7.2",
            NewVersion = SemanticVersion.Parse("1.7.2"),
            ReleaseNotes = "Notes"
        };

        MergeAndPublishStep step = CreateStep();
        await step.ExecuteAsync(context, targetBranch: "main");

        _processMock.Verify(p => p.ExecuteAsync(
            "git",
            It.Is<IReadOnlyList<string>>(args => args.SequenceEqual(new[] { "merge", "--ff-only", "release-prep/v1.7.2" })),
            _projectContext.WorkingDirectory,
            It.IsAny<Action<string>?>(),
            It.IsAny<Action<string>?>(),
            It.IsAny<CancellationToken>()), Times.Once);

        _processMock.Verify(p => p.ExecuteAsync(
            "git",
            It.Is<IReadOnlyList<string>>(args => args.SequenceEqual(new[] { "merge", "--ff-only", "main" })),
            _projectContext.WorkingDirectory,
            It.IsAny<Action<string>?>(),
            It.IsAny<Action<string>?>(),
            It.IsAny<CancellationToken>()), Times.Once);

        _processMock.Verify(p => p.ExecuteAsync(
            "git",
            It.Is<IReadOnlyList<string>>(args => args.Count > 0 && args[0] == "commit"),
            _projectContext.WorkingDirectory,
            It.IsAny<Action<string>?>(),
            It.IsAny<Action<string>?>(),
            It.IsAny<CancellationToken>()), Times.Never);

        _gitMock.Verify(g => g.TagAndPushAsync("v1.7.2", It.IsAny<CancellationToken>()), Times.Once);
        _publisherMock.Verify(p => p.PublishAsync(
            It.Is<ReleasePublicationRequest>(r => r.TagName == "v1.7.2" && !r.IsPreRelease),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_DivergedTargetBranch_FallsBackToForcedMerge()
    {
        _processMock
            .Setup(p => p.ExecuteAsync(
                "git",
                It.IsAny<IReadOnlyList<string>>(),
                _projectContext.WorkingDirectory,
                It.IsAny<Action<string>?>(),
                It.IsAny<Action<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, IReadOnlyList<string> args, string _, Action<string>? _, Action<string>? _, CancellationToken _) =>
                args.SequenceEqual(new[] { "merge", "--ff-only", "release-prep/v1.7.2" }) ? 1 : 0);

        var context = new PipelineContext
        {
            SourceBranch = "main",
            ReleaseBranch = "release-prep/v1.7.2",
            NewVersion = SemanticVersion.Parse("1.7.2"),
            ReleaseNotes = "Notes"
        };

        MergeAndPublishStep step = CreateStep();
        await step.ExecuteAsync(context, targetBranch: "main");

        _processMock.Verify(p => p.ExecuteAsync(
            "git",
            It.Is<IReadOnlyList<string>>(args => args.SequenceEqual(new[] { "merge", "--no-commit", "--no-ff", "release-prep/v1.7.2" })),
            _projectContext.WorkingDirectory,
            It.IsAny<Action<string>?>(),
            It.IsAny<Action<string>?>(),
            It.IsAny<CancellationToken>()), Times.Once);

        _processMock.Verify(p => p.ExecuteAsync(
            "git",
            It.Is<IReadOnlyList<string>>(args => args.SequenceEqual(new[] { "commit", "-m", "chore: merge release v1.7.2" })),
            _projectContext.WorkingDirectory,
            It.IsAny<Action<string>?>(),
            It.IsAny<Action<string>?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}