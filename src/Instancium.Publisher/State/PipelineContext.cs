using Instancium.Publisher.Core.Domain;

namespace Instancium.Publisher.State;

public class PipelineContext
{
    public int CurrentStep { get; set; } = 0;
    public string SourceBranch { get; set; } = string.Empty;
    public string ReleaseBranch { get; set; } = string.Empty;
    public SemanticVersion? CurrentVersion { get; set; }
    public SemanticVersion? NewVersion { get; set; }
    public string CommitMessage { get; set; } = string.Empty;
    public string ReleaseNotes { get; set; } = string.Empty;
}