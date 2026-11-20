namespace Instancium.Publisher.Tests.Services;

using Instancium.Publisher.Infrastructure.Versioning;
using Instancium.Publisher.Infrastructure.Versioning.DotNet;
using Instancium.Publisher.Tests.TestInfrastructure;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

/// <summary>
/// Verifies repository-scoped project discovery: recursive search of ecosystem manifests,
/// exclusion of build outputs and tooling directories, deterministic ordering and failure modes.
/// </summary>
public sealed class RepositoryProjectScannerTests
{
    private readonly RepositoryProjectScanner _scanner = new([new MsBuildProjectEcosystem()]);

    [Fact]
    public async Task DiscoverProjectFilesAsync_FindsProjectsRecursivelyInSortedOrder()
    {
        using var scope = new TemporaryDirectoryScope();
        string rootProject = CsprojFileFactory.Create(scope.RootPath, "App.csproj", version: null);
        string nestedProject = CsprojFileFactory.Create(scope.CreateDirectory("src/Core"), "Core.csproj", version: null);

        IReadOnlyList<string> discovered = await _scanner.DiscoverProjectFilesAsync(scope.RootPath);

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

        IReadOnlyList<string> discovered = await _scanner.DiscoverProjectFilesAsync(scope.RootPath);

        Assert.Equal(new[] { validProject }, discovered);
    }

    [Fact]
    public async Task DiscoverProjectFilesAsync_EmptyRepositoryRoot_ReturnsEmptyList()
    {
        using var scope = new TemporaryDirectoryScope();
        scope.CreateDirectory("src/Empty");

        IReadOnlyList<string> discovered = await _scanner.DiscoverProjectFilesAsync(scope.RootPath);

        Assert.Empty(discovered);
    }

    [Fact]
    public async Task DiscoverProjectFilesAsync_MissingRepositoryRoot_ThrowsDirectoryNotFoundException()
    {
        using var scope = new TemporaryDirectoryScope();
        string missingRoot = Path.Combine(scope.RootPath, "does-not-exist");

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => _scanner.DiscoverProjectFilesAsync(missingRoot));
    }

    [Fact]
    public async Task DiscoverProjectFilesAsync_NullOrWhitespaceRoot_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _scanner.DiscoverProjectFilesAsync(string.Empty));
        await Assert.ThrowsAsync<ArgumentException>(() => _scanner.DiscoverProjectFilesAsync("   "));
    }

    [Fact]
    public async Task DiscoverProjectFilesAsync_CancellationRequested_ThrowsOperationCanceled()
    {
        using var scope = new TemporaryDirectoryScope();
        CsprojFileFactory.Create(scope.RootPath, "App.csproj", version: null);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _scanner.DiscoverProjectFilesAsync(scope.RootPath, cts.Token));
    }
}
