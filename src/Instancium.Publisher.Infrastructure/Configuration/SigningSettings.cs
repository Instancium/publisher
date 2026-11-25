namespace Instancium.Publisher.Infrastructure.Configuration;

/// <summary>
/// Configuration locating the local encrypted Release Key container and its certificate.
/// Passwords are never stored here; they are resolved at runtime via <see cref="Application.Abstractions.ISecretProvider"/>.
/// </summary>
public sealed class SigningSettings
{
    public string ReleaseKeyContainerPath { get; set; } = string.Empty;
    public string ReleaseCertificatePath { get; set; } = string.Empty;
    public string ReleaseKeyId { get; set; } = string.Empty;
    public string PasswordEnvironmentVariable { get; set; } = "INSTANCIUM_RELEASE_KEY_PASSWORD";
}
