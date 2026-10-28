using System;

namespace Instancium.Publisher.Core.Exceptions;

/// <summary>
/// Thrown when a discovered project file exists on disk but cannot be parsed as MSBuild XML.
/// Carries the offending path so the TUI can report an actionable, file-scoped error.
/// </summary>
public sealed class ProjectFileParseException : Exception
{
    public ProjectFileParseException(string projectPath, string reason, Exception? innerException = null)
        : base($"Project file '{projectPath}' is not valid MSBuild XML. {reason}", innerException)
    {
        ProjectPath = projectPath;
    }

    public string ProjectPath { get; }
}