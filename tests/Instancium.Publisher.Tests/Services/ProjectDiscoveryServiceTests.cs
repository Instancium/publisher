namespace Instancium.Publisher.Tests.Services;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Instancium.Publisher.Services.Versioning;
using Instancium.Publisher.Tests.TestInfrastructure;
using Xunit;

/// <summary>
/// Verifies repository-scoped project discovery: recursive search of SDK-style projects,
/// exclusion of build outputs and tooling directories, deterministic ordering and failure modes.
/// The service is fully asynchronous: every failure surfaces through the awaited task.
/// </summary>
public sealed class ProjectDiscoveryServiceTests
{
    private readonly ProjectDiscoveryService _service = new();

    [Fact]
    public async Task DiscoverProjectFilesAsync_FindsProjectsRecursivelyInSortedOrder()
    {
        using var scope = new TemporaryDirectoryScope();
        string rootProject = CsprojFileFactory.Create(scope.RootPath, "App.csproj", version: null);
        string nestedProject = CsprojFileFactory.Create(scope.CreateDirectory("src/Core"), "Core.csproj", version: null);

        IReadOnlyList<string> discovered = await _service.DiscoverProjectFilesAsync(scope.RootPath);

        Assert.Equal(new[] { rootProject, nestedProject }, discovered);
    }

    [Fact]
    public async Task DiscoverProjectFilesAsync_ExcludesBuildAndToolingDirectories()
    {
        using var scope = new TemporaryDirectoryScope();
        string validProject = CsprojFileFactory.Create(scope.CreateDirectory("src/Core"), "Core.csproj", version: null);
        CsprojFileFactory.Create(scope.CreateDirectory("src/Core/bin/Debug"), "Core.csproj", version: null);
        CsprojFileFactory.Create(scope.CreateDirectory("src/Core/obj"), "Core.csproj", version: null);
        CsprojFileFactory.Create(scope.CreateDirectory(".git/hooks"), "Hook.csproj", version: null);
        CsprojFileFactory.Create(scope.CreateDirectory(".vs"), "Vs.csproj", version: null);
        CsprojFileFactory.Create(scope.CreateDirectory("node_modules/pkg"), "Pkg.csproj", version: null);

        IReadOnlyList<string> discovered = await _service.DiscoverProjectFilesAsync(scope.RootPath);

        Assert.Equal(new[] { validProject }, discovered);
    }

    [Fact]
    public async Task DiscoverProjectFilesAsync_EmptyRepositoryRoot_ReturnsEmptyList()
    {
        using var scope = new TemporaryDirectoryScope();
        scope.CreateDirectory("src/Empty");

        IReadOnlyList<string> discovered = await _service.DiscoverProjectFilesAsync(scope.RootPath);

        Assert.Empty(discovered);
    }

    [Fact]
    public async Task DiscoverProjectFilesAsync_MissingRepositoryRoot_ThrowsDirectoryNotFoundException()
    {
        using var scope = new TemporaryDirectoryScope();
        string missingRoot = Path.Combine(scope.RootPath, "does-not-exist");

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => _service.DiscoverProjectFilesAsync(missingRoot));
    }

    [Fact]
    public async Task DiscoverProjectFilesAsync_NullOrWhitespaceRoot_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.DiscoverProjectFilesAsync(string.Empty));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.DiscoverProjectFilesAsync("   "));
    }

    [Fact]
    public async Task DiscoverProjectFilesAsync_CancellationRequested_ThrowsOperationCanceled()
    {
        using var scope = new TemporaryDirectoryScope();
        CsprojFileFactory.Create(scope.RootPath, "App.csproj", version: null);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _service.DiscoverProjectFilesAsync(scope.RootPath, cts.Token));
    }
}