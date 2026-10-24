namespace Instancium.Publisher.State;

using System;

public sealed class ProjectContext
{
    public string Name { get; set; } = "Local Directory";
    public string WorkingDirectory { get; set; } = Environment.CurrentDirectory;
    public string? ProjectFilePath { get; set; }
}