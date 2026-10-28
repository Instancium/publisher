namespace Instancium.Publisher.Tests.TestInfrastructure;

using System.IO;
using System.Linq;
using System.Xml.Linq;

/// <summary>
/// Writes minimal SDK-style project files used by versioning and discovery tests.
/// </summary>
public static class CsprojFileFactory
{
    public static string Create(string directoryPath, string fileName, string? version)
    {
        Directory.CreateDirectory(directoryPath);
        string versionElement = version is null ? string.Empty : $"    <Version>{version}</Version>\n";
        string content = $"""
<?xml version="1.0" encoding="utf-8"?>
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
{versionElement}  </PropertyGroup>
</Project>
""";
        string fullPath = Path.Combine(directoryPath, fileName);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    public static string CreateWithoutPropertyGroup(string directoryPath, string fileName)
    {
        Directory.CreateDirectory(directoryPath);
        const string content = """
<?xml version="1.0" encoding="utf-8"?>
<Project Sdk="Microsoft.NET.Sdk">
</Project>
""";
        string fullPath = Path.Combine(directoryPath, fileName);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    public static string ReadVersionElementValue(string projectPath)
    {
        XDocument document = XDocument.Load(projectPath);
        return document.Root?.Descendants("Version").FirstOrDefault()?.Value ?? string.Empty;
    }

    public static bool HasPropertyGroup(string projectPath)
    {
        XDocument document = XDocument.Load(projectPath);
        return document.Root?.Element("PropertyGroup") is not null;
    }
}
