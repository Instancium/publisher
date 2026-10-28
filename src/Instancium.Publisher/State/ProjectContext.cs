namespace Instancium.Publisher.State;

using System;

/// <summary>
/// Runtime context of the selected target repository.
/// Versioning is repository-scoped, therefore a single project file path is no longer part of the context.
/// </summary>
public sealed class ProjectContext
{
    public string Name { get; set; } = "Local Directory";
    public string WorkingDirectory { get; set; } = Environment.CurrentDirectory;
}