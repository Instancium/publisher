namespace Instancium.Publisher.Tests.Services;

using Instancium.Publisher.Domain.Versioning;
using Instancium.Publisher.Infrastructure.Versioning.DotNet;
using Instancium.Publisher.Tests.TestInfrastructure;
using System.IO;
using System.Threading.Tasks;
using System.Xml.Linq;
using Xunit;

/// <summary>
/// Verifies MSBuild ecosystem reading and writing against real SDK-style project files on disk.
/// </summary>
public sealed class MsBuildProjectEcosystemTests
{
    private readonly MsBuildProjectEcosystem _ecosystem = new();

    [Fact]
    public async Task ReadVersionAsync_DeclaredVersion_ReturnsSnapshotWithVersion()
    {
        using var scope = new TemporaryDirectoryScope();
        string projectPath = CsprojFileFactory.Create(scope.RootPath, "Sample.csproj", version: "2.3.4-rc.2");

        ProjectVersionSnapshot snapshot = await _ecosystem.ReadVersionAsync(projectPath);

        Assert.Equal(projectPath, snapshot.ProjectPath);
        Assert.True(snapshot.HasDeclaredVersion);
        Assert.Equal("2.3.4-rc.2", snapshot.DeclaredVersion?.ToString());
    }

    [Fact]
    public async Task ReadVersionAsync_MissingVersionElement_ReturnsNullDeclaredVersion()
    {
        using var scope = new TemporaryDirectoryScope();
        string projectPath = CsprojFileFactory.Create(scope.RootPath, "Sample.csproj", version: null);

        ProjectVersionSnapshot snapshot = await _ecosystem.ReadVersionAsync(projectPath);

        // Regression guard: absence of <Version> must NOT silently become 1.0.0.
        Assert.False(snapshot.HasDeclaredVersion);
        Assert.Null(snapshot.DeclaredVersion);
    }

    [Fact]
    public async Task ReadVersionAsync_MissingFile_ThrowsFileNotFoundException()
    {
        using var scope = new TemporaryDirectoryScope();
        string missingPath = Path.Combine(scope.RootPath, "Missing.csproj");

        await Assert.ThrowsAsync<FileNotFoundException>(() => _ecosystem.ReadVersionAsync(missingPath));
    }

    [Fact]
    public async Task WriteVersionAsync_ProjectWithoutPropertyGroup_AddsPropertyGroupWithVersion()
    {
        using var scope = new TemporaryDirectoryScope();
        string projectPath = CsprojFileFactory.CreateWithoutPropertyGroup(scope.RootPath, "Bare.csproj");
        var targetVersion = SemanticVersion.Parse("3.1.4");

        await _ecosystem.WriteVersionAsync(projectPath, targetVersion);

        Assert.Equal("3.1.4", CsprojFileFactory.ReadVersionElementValue(projectPath));
        Assert.True(CsprojFileFactory.HasPropertyGroup(projectPath));
        XDocument document = XDocument.Load(projectPath);
        XElement? propertyGroup = document.Root?.Element("PropertyGroup");
        Assert.Equal("3.1.4", propertyGroup?.Element("Version")?.Value);
    }
}
