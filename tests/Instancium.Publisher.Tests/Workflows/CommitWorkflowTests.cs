namespace Instancium.Publisher.Tests.Workflows;

using Instancium.Publisher.Application.Abstractions;
using Instancium.Publisher.Application.Workflows;
using Moq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

/// <summary>
/// Verifies P1 commit boundary guarantees: partial staging preservation and
/// consistent commit message sanitization prior to Git commit execution.
/// </summary>
public sealed class CommitWorkflowTests
{
    private readonly Mock<IGitService> _gitMock = new();
    private readonly Mock<IAiCommitAssistant> _aiMock = new();

    [Fact]
    public async Task StageAndInspectAsync_WhenChangesAlreadyStaged_DoesNotOverwriteIndexWithAddAll()
    {
        _gitMock.Setup(g => g.GetCachedDiffAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("diff --git a/src/A.cs b/src/A.cs\n+code");
        _gitMock.Setup(g => g.GetCachedDiffStatAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(" src/A.cs | 1 +");

        var workflow = new CommitWorkflow(_gitMock.Object, _aiMock.Object);

        CommitInspectionResult result = await workflow.StageAndInspectAsync();

        Assert.True(result.HasChanges);
        Assert.Contains("src/A.cs", result.Diff);
        _gitMock.Verify(g => g.AddAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StageAndInspectAsync_WhenIndexIsEmpty_StagesAllAndInspects()
    {
        _gitMock.SetupSequence(g => g.GetCachedDiffAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty)
            .ReturnsAsync("diff --git a/src/B.cs b/src/B.cs\n+code");
        _gitMock.Setup(g => g.GetCachedDiffStatAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(" src/B.cs | 1 +");

        var workflow = new CommitWorkflow(_gitMock.Object, _aiMock.Object);

        CommitInspectionResult result = await workflow.StageAndInspectAsync();

        Assert.True(result.HasChanges);
        _gitMock.Verify(g => g.AddAllAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CommitAndGetBranchAsync_SanitizesPlaceholdersBeforeCommitting()
    {
        _gitMock.Setup(g => g.GetCurrentBranchAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("dev");

        var workflow = new CommitWorkflow(_gitMock.Object, _aiMock.Object);

        string branch = await workflow.CommitAndGetBranchAsync("feat(git): enforce boundaries\n<no-body>\n- Add merged head check");

        Assert.Equal("dev", branch);
        _gitMock.Verify(g => g.CommitAsync(
            It.Is<string>(msg =>
                !msg.Contains("<no-body>") &&
                msg.StartsWith("feat(git): enforce boundaries") &&
                msg.Contains("- Add merged head check")),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}