namespace Instancium.Publisher.Domain.Versioning;

/// <summary>Optional inputs for a version bump: pre-release tag and an explicit custom version.</summary>
public sealed record VersionBumpContext(string PreReleaseTag = "rc", string? CustomVersion = null);
