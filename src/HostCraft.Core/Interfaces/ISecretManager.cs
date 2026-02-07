using HostCraft.Core.Entities;

namespace HostCraft.Core.Interfaces;

/// <summary>
/// Service for managing encrypted secrets (environment variables and private keys).
/// </summary>
public interface ISecretManager
{
    /// <summary>
    /// Creates or updates an environment variable, encrypting if it's a secret.
    /// </summary>
    Task<EnvironmentVariable> SetEnvironmentVariableAsync(
        int applicationId,
        string key,
        string value,
        bool isSecret,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an environment variable, decrypting if it's a secret.
    /// </summary>
    Task<EnvironmentVariable?> GetEnvironmentVariableAsync(
        int applicationId,
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all environment variables for an application.
    /// Secrets are decrypted, or optionally masked.
    /// </summary>
    Task<IEnumerable<EnvironmentVariable>> GetEnvironmentVariablesAsync(
        int applicationId,
        bool decryptSecrets = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an environment variable.
    /// </summary>
    Task<bool> DeleteEnvironmentVariableAsync(
        int applicationId,
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or updates a private key with encrypted storage.
    /// </summary>
    Task<PrivateKey> SetPrivateKeyAsync(
        string name,
        string keyData,
        string? passphrase = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a private key with decrypted key data.
    /// </summary>
    Task<PrivateKey?> GetPrivateKeyAsync(
        int keyId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a private key by name with decrypted key data.
    /// </summary>
    Task<PrivateKey?> GetPrivateKeyByNameAsync(
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a private key.
    /// </summary>
    Task<bool> DeletePrivateKeyAsync(
        int keyId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-encrypts all secrets with a new key (for key rotation).
    /// </summary>
    Task<int> RotateEncryptionKeyAsync(
        string newKey,
        CancellationToken cancellationToken = default);
}
