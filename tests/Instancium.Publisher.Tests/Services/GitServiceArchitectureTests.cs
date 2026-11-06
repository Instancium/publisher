namespace Instancium.Publisher.Tests.Services;

using Instancium.Publisher.Configuration;
using Instancium.Publisher.Core.Interfaces;
using Instancium.Publisher.Services.Git;
using Instancium.Publisher.State;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Xunit;

/// <summary>
/// Architecture Guard Tests.
/// These tests protect the core version-aggregation strategy designed for the Changelog pipeline.
/// They ensure that future refactoring does not accidentally break the topological tag resolution
/// or the pre-release aggregation mechanism.
/// </summary>
public sealed class GitServiceArchitectureTests
{
    private readonly Mock<IProcessExecutionService> _processMock;
    private readonly Mock<IProgressReporter> _reporterMock;
    private readonly IOptions<PublisherSettings> _settings;
    private readonly ProjectContext _context;
    private readonly GitService _gitService;

    public GitServiceArchitectureTests()
    {
        _processMock = new Mock<IProcessExecutionService>();
        _reporterMock = new Mock<IProgressReporter>();

        _settings = Options.Create(new PublisherSettings
        {
            DiffOptimization = new DiffOptimizationSettings { Enabled = false }
        });

        _context = new ProjectContext { WorkingDirectory = "/test/repo" };

        _gitService = new GitService(
            _processMock.Object,
            _context,
            _reporterMock.Object,
            NullLogger<GitService>.Instance,
            _settings);
    }

    [Fact]
    public async Task GetCommitLogAsync_StableRelease_MustExcludePreReleaseTagsToAggregateHistory()
    {
        // Arrange
        // We simulate the workflow when compiling a stable release (e.g., v1.6.0).
        // The architectural requirement is to aggregate all commits across previous pre-releases (rc.1, rc.2).
        string fromTag = "v1.5.0";
        bool ignorePreReleaseTags = true; // This flag signifies a stable release build

        _processMock
            .Setup(p => p.GetOutputAsync("git", It.IsAny<string>(), _context.WorkingDirectory, It.IsAny<CancellationToken>()))
            .ReturnsAsync("commit-log-data");

        // Act
        await _gitService.GetCommitLogAsync(fromTag, ignorePreReleaseTags);

        // Assert
        // The CRITICAL architectural constraint: 
        // When ignorePreReleaseTags is true, the Git command MUST include:
        // 1. --not --tags (to exclude past stable features)
        // 2. --exclude="*-*" (to pierce through the boundaries of intermediate -rc.* tags and aggregate them)
        _processMock.Verify(p => p.GetOutputAsync(
            "git",
            It.Is<string>(args =>
                args.Contains($"{fromTag}..HEAD") &&
                args.Contains("--not") &&
                args.Contains("--exclude=\"*-*\"") &&
                args.Contains("--tags")),
            _context.WorkingDirectory,
            It.IsAny<CancellationToken>()),
            Times.Once,
            "ARCHITECTURAL REGRESSION: The stable release changelog query MUST include '--not --exclude=\"*-*\" --tags' to correctly aggregate pre-release history.");
    }

    [Fact]
    public async Task GetCommitLogAsync_PreRelease_MustExcludeAllTags()
    {
        // Arrange
        // We simulate the workflow when compiling a pre-release (e.g., v1.6.0-rc.2).
        // The architectural requirement is to fetch only the delta from the previous tag (e.g., v1.6.0-rc.1).
        string fromTag = "v1.6.0-rc.1";
        bool ignorePreReleaseTags = false; // This flag signifies a pre-release build

        _processMock
            .Setup(p => p.GetOutputAsync("git", It.IsAny<string>(), _context.WorkingDirectory, It.IsAny<CancellationToken>()))
            .ReturnsAsync("commit-log-data");

        // Act
        await _gitService.GetCommitLogAsync(fromTag, ignorePreReleaseTags);

        // Assert
        // The CRITICAL architectural constraint: 
        // When ignorePreReleaseTags is false, the Git command MUST include:
        // 1. --not --tags
        // It MUST NOT include --exclude="*-*" because we want the pipeline to stop exactly at the previous -rc.* tag.
        _processMock.Verify(p => p.GetOutputAsync(
            "git",
            It.Is<string>(args =>
                args.Contains($"{fromTag}..HEAD") &&
                args.Contains("--not --tags") &&
                !args.Contains("--exclude=\"*-*\"")),
            _context.WorkingDirectory,
            It.IsAny<CancellationToken>()),
            Times.Once,
            "ARCHITECTURAL REGRESSION: The pre-release changelog query MUST include strictly '--not --tags' without exclusions, to capture only the delta from the previous release candidate.");
    }
}