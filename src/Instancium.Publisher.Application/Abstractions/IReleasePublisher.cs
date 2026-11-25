namespace Instancium.Publisher.Application.Abstractions;

/// <summary>Request to publish a release for an already pushed tag with optional release assets.</summary>
public sealed record ReleasePublicationRequest(
    string TagName,
    string Title,
    string Notes,
    bool IsPreRelease,
    IReadOnlyList<string>? AssetPaths = null);

/// <summary>Extension point for release publication targets (GitHub CLI today, others later).</summary>
public interface IReleasePublisher
{
    Task PublishAsync(ReleasePublicationRequest request, CancellationToken ct = default);
}
