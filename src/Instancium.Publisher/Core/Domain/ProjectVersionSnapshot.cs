namespace Instancium.Publisher.Core.Domain;

/// <summary>
/// Immutable snapshot of the version declared inside a single SDK-style project file.
/// A null <see cref="DeclaredVersion"/> means the project does not declare a version at all,
/// which must be distinguished from an explicitly declared 1.0.0.
/// </summary>
public sealed record ProjectVersionSnapshot(string ProjectPath, SemanticVersion? DeclaredVersion)
{
    public bool HasDeclaredVersion => DeclaredVersion is not null;
}
