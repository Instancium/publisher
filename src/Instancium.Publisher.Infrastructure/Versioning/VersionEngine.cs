using Instancium.Publisher.Domain.Exceptions;
using Instancium.Publisher.Application.Abstractions;
using Instancium.Publisher.Domain.Versioning;
using System.Xml;
using System.Xml.Linq;

namespace Instancium.Publisher.Infrastructure.Versioning;

public sealed class VersionEngine : IVersionEngine
{
    public async Task<ProjectVersionSnapshot> ReadProjectVersionAsync(string projectPath, CancellationToken ct = default)
    {
        XDocument document = await LoadDocumentAsync(projectPath, ct);
        XElement? versionElement = document.Root?.Descendants("Version").FirstOrDefault();
        SemanticVersion? declaredVersion = string.IsNullOrWhiteSpace(versionElement?.Value)
            ? null
            : SemanticVersion.Parse(versionElement.Value);
        return new ProjectVersionSnapshot(projectPath, declaredVersion);
    }

    public async Task<IReadOnlyList<ProjectVersionSnapshot>> ReadProjectVersionsAsync(
        IEnumerable<string> projectPaths,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(projectPaths);
        var snapshots = new List<ProjectVersionSnapshot>();
        foreach (string projectPath in projectPaths)
        {
            snapshots.Add(await ReadProjectVersionAsync(projectPath, ct));
        }
        return snapshots;
    }

    public async Task WriteVersionToProjectsAsync(
        IEnumerable<string> projectPaths,
        SemanticVersion version,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(projectPaths);
        ArgumentNullException.ThrowIfNull(version);
        foreach (string projectPath in projectPaths)
        {
            await WriteVersionToProjectInternalAsync(projectPath, version, ct);
        }
    }

    private static async Task<XDocument> LoadDocumentAsync(string projectPath, CancellationToken ct)
    {
        if (!File.Exists(projectPath))
        {
            throw new FileNotFoundException($"Project file not found: '{projectPath}'.", projectPath);
        }

        try
        {
            await using var stream = new FileStream(projectPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            return await XDocument.LoadAsync(stream, LoadOptions.None, ct);
        }
        catch (XmlException ex)
        {
            throw new ProjectFileParseException(projectPath, ex.Message, ex);
        }
    }

    private static async Task WriteVersionToProjectInternalAsync(string projectPath, SemanticVersion version, CancellationToken ct)
    {
        XDocument document = await LoadDocumentAsync(projectPath, ct);
        XElement? root = document.Root;
        if (root is null)
        {
            throw new ProjectFileParseException(projectPath, "XML document has no root element.");
        }

        XElement? versionElement = root.Descendants("Version").FirstOrDefault();
        if (versionElement != null)
        {
            versionElement.Value = version.ToString();
        }
        else
        {
            XElement? propertyGroup = root.Element("PropertyGroup");
            if (propertyGroup == null)
            {
                propertyGroup = new XElement("PropertyGroup");
                root.AddFirst(propertyGroup);
            }
            propertyGroup.Add(new XElement("Version", version.ToString()));
        }

        await using var writeStream = new FileStream(projectPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await document.SaveAsync(writeStream, SaveOptions.None, ct);
    }
}