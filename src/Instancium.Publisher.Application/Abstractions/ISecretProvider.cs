namespace Instancium.Publisher.Application.Abstractions;

/// <summary>
/// Supplies runtime secrets (such as the encrypted release key container password)
/// without persisting credentials in configuration files or coupling infrastructure to UI prompts.
/// </summary>
public interface ISecretProvider
{
    /// <summary>
    /// Resolves the password required to unlock the local release key container.
    /// </summary>
    Task<string> GetReleaseKeyPasswordAsync(CancellationToken ct = default);
}
