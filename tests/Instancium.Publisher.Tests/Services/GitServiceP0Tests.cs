namespace Instancium.Publisher.Tests.Services;

using Instancium.Publisher.Application.Abstractions;
using Instancium.Publisher.Application.State;
using Instancium.Publisher.Infrastructure.Configuration;
using Instancium.Publisher.Infrastructure.Git;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

/// <summary>
/// Verifies P0 Git boundary guarantees: topological HEAD-merged tag lookup,
/// structured commit log formatting, and pathspec exclusion of noisy artifacts.
/// </summary>
public sealed class GitServiceP0Tests
{
    private readonly Mock<IProcessExecutionService> _processMock = new();
    private readonly Mock<IProgressReporter> _reporterMock = new();
    private readonly ProjectContext _context = new() { WorkingDirectory = "/test/repo" };

    private GitService CreateService(DiffOptimizationSettings? diffSettings = null)
    {
        var settings = Options.Create(new PublisherSettings
        {
            DiffOptimization = diffSettings ?? new DiffOptimizationSettings()
        });

        return new GitService(
            _processMock.Object,
            _context,
            _reporterMock.Object,
            NullLogger<GitService>.Instance,
            settings);
    }

    [Fact]
    public async Task GetLatestTagAsync_UsesMergedHeadAndSkipsPreReleaseWhenStableOnly()
    {
        _processMock
            .Setup(p => p.GetOutputAsync("git", It.IsAny<IReadOnlyList<string>>(), _context.WorkingDirectory, It.IsAny<CancellationToken>()))
            .ReturnsAsync("v2.0.0-rc.1\nv1.7.0\nv1.6.7\n");

        GitService service = CreateService();

        string latest = await service.GetLatestTagAsync(stableOnly: true);

        Assert.Equal("v1.7.0", latest);
        _processMock.Verify(p => p.GetOutputAsync(
            "git",
            It.Is<IReadOnlyList<string>>(args =>
                args.SequenceEqual(new[] { "tag", "--merged", "HEAD", "--sort=-v:refname" })),
            _context.WorkingDirectory,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetLatestTagAsync_StableOnlyWithOnlyPreReleaseTags_ReturnsEmpty()
    {
        _processMock
            .Setup(p => p.GetOutputAsync("git", It.IsAny<IReadOnlyList<string>>(), _context.WorkingDirectory, It.IsAny<CancellationToken>()))
            .ReturnsAsync("v1.0.0-rc.2\nv1.0.0-rc.1\n");

        GitService service = CreateService();

        string latest = await service.GetLatestTagAsync(stableOnly: true);

        Assert.Equal(string.Empty, latest);
    }

    [Fact]
    public async Task GetCommitLogAsync_UsesStructuredFormatWithBodyAndStatAndPathspecsAtEnd()
    {
        _processMock
            .Setup(p => p.GetOutputAsync("git", It.IsAny<IReadOnlyList<string>>(), _context.WorkingDirectory, It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);

        GitService service = CreateService(new DiffOptimizationSettings
        {
            Enabled = true,
            ExcludedPathspecs = ["*.lock", "*.g.cs"]
        });

        await service.GetCommitLogAsync("v1.6.7", ignorePreReleaseTags: true);

        _processMock.Verify(p => p.GetOutputAsync(
            "git",
            It.Is<IReadOnlyList<string>>(args =>
                args.Count >= 4 &&
                args.Contains("--format=%x1e%h%x1f%s%x1f%b%x1f") &&
                args.Contains("--stat=120,100") &&
                args.Contains("v1.6.7..HEAD") &&
                args.Contains("--not") &&
                args.Contains("--exclude=*-*") &&
                args.Contains("--tags") &&
                args[args.Count - 4] == "--" &&
                args[args.Count - 3] == "." &&
                args[args.Count - 2] == ":(exclude)*.lock" &&
                args[args.Count - 1] == ":(exclude)*.g.cs"),
            _context.WorkingDirectory,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetCachedDiffAsync_AppendsContextLinesAndExcludedPathspecs()
    {
        _processMock
            .Setup(p => p.GetOutputAsync("git", It.IsAny<IReadOnlyList<string>>(), _context.WorkingDirectory, It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);

        GitService service = CreateService(new DiffOptimizationSettings
        {
            Enabled = true,
            ContextLines = 3,
            IgnoreWhitespace = true,
            ExcludedPathspecs = ["package-lock.json"]
        });

        await service.GetCachedDiffAsync();

        _processMock.Verify(p => p.GetOutputAsync(
            "git",
            It.Is<IReadOnlyList<string>>(args =>
                args.Contains("-U3") &&
                args.Contains("-w") &&
                args.Contains("--") &&
                args.Contains(":(exclude)package-lock.json")),
            _context.WorkingDirectory,
            It.IsAny<CancellationToken>()), Times.Once);
    }
}