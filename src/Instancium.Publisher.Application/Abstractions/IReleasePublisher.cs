namespace Instancium.Publisher.Application.Abstractions;

/// <summary>Request to publish a release for an already pushed tag.</summary>
public sealed record ReleasePublicationRequest(string TagName, string Title, string Notes, bool IsPreRelease);

/// <summary>Extension point for release publication targets (GitHub CLI today, others later).</summary>
public interface IReleasePublisher
{
    Task PublishAsync(ReleasePublicationRequest request, CancellationToken ct = default);
}
