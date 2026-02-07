namespace HostCraft.Core.Interfaces;

/// <summary>
/// Service for encrypting and decrypting sensitive data at rest.
/// </summary>
public interface IEncryptionService
{
    /// <summary>
    /// Encrypts a plaintext string.
    /// </summary>
    string Encrypt(string plainText);

    /// <summary>
    /// Decrypts an encrypted string.
    /// </summary>
    string Decrypt(string cipherText);

    /// <summary>
    /// Checks if a string appears to be encrypted (starts with encryption marker).
    /// </summary>
    bool IsEncrypted(string value);

    /// <summary>
    /// Generates a new encryption key.
    /// </summary>
    string GenerateKey();
}
