using System.Security.Cryptography;
using System.Text;
using HostCraft.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HostCraft.Infrastructure.Security;

/// <summary>
/// AES-256-GCM encryption service for securing sensitive data at rest.
/// </summary>
public class EncryptionService : IEncryptionService
{
    private readonly byte[] _key;
    private readonly ILogger<EncryptionService> _logger;

    // Marker prefix to identify encrypted values
    private const string EncryptionMarker = "ENC:";

    public EncryptionService(IConfiguration configuration, ILogger<EncryptionService> logger)
    {
        _logger = logger;

        // Get encryption key from configuration or generate one
        var keyString = configuration["Encryption:Key"];

        if (string.IsNullOrEmpty(keyString))
        {
            _logger.LogWarning("Encryption key not configured - generating temporary key. " +
                              "Set Encryption:Key in configuration for persistent encryption.");
            _key = GenerateKeyBytes();
        }
        else
        {
            try
            {
                _key = Convert.FromBase64String(keyString);
                if (_key.Length != 32)
                {
                    throw new InvalidOperationException("Encryption key must be 32 bytes (256 bits)");
                }
            }
            catch (FormatException)
            {
                throw new InvalidOperationException("Encryption:Key must be a valid base64 string of 32 bytes");
            }
        }
    }

    public string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            return plainText;
        }

        // Don't double-encrypt
        if (IsEncrypted(plainText))
        {
            return plainText;
        }

        try
        {
            var plainBytes = Encoding.UTF8.GetBytes(plainText);

            // Generate a random nonce (12 bytes for AES-GCM)
            var nonce = new byte[12];
            RandomNumberGenerator.Fill(nonce);

            // Encrypt with AES-GCM
            var cipherText = new byte[plainBytes.Length];
            var tag = new byte[16]; // 128-bit authentication tag

            using var aesGcm = new AesGcm(_key, 16);
            aesGcm.Encrypt(nonce, plainBytes, cipherText, tag);

            // Combine nonce + tag + ciphertext
            var result = new byte[nonce.Length + tag.Length + cipherText.Length];
            Buffer.BlockCopy(nonce, 0, result, 0, nonce.Length);
            Buffer.BlockCopy(tag, 0, result, nonce.Length, tag.Length);
            Buffer.BlockCopy(cipherText, 0, result, nonce.Length + tag.Length, cipherText.Length);

            return EncryptionMarker + Convert.ToBase64String(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to encrypt data");
            throw new InvalidOperationException("Encryption failed", ex);
        }
    }

    public string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
        {
            return cipherText;
        }

        // Check if actually encrypted
        if (!IsEncrypted(cipherText))
        {
            return cipherText;
        }

        try
        {
            // Remove marker and decode
            var encryptedData = Convert.FromBase64String(cipherText[EncryptionMarker.Length..]);

            // Extract nonce, tag, and ciphertext
            var nonce = new byte[12];
            var tag = new byte[16];
            var cipherBytes = new byte[encryptedData.Length - nonce.Length - tag.Length];

            Buffer.BlockCopy(encryptedData, 0, nonce, 0, nonce.Length);
            Buffer.BlockCopy(encryptedData, nonce.Length, tag, 0, tag.Length);
            Buffer.BlockCopy(encryptedData, nonce.Length + tag.Length, cipherBytes, 0, cipherBytes.Length);

            // Decrypt
            var plainBytes = new byte[cipherBytes.Length];

            using var aesGcm = new AesGcm(_key, 16);
            aesGcm.Decrypt(nonce, cipherBytes, tag, plainBytes);

            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (CryptographicException ex)
        {
            _logger.LogError(ex, "Decryption failed - data may be corrupted or key mismatch");
            throw new InvalidOperationException("Decryption failed - data may be corrupted or wrong key", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt data");
            throw new InvalidOperationException("Decryption failed", ex);
        }
    }

    public bool IsEncrypted(string value)
    {
        return !string.IsNullOrEmpty(value) && value.StartsWith(EncryptionMarker);
    }

    public string GenerateKey()
    {
        return Convert.ToBase64String(GenerateKeyBytes());
    }

    private static byte[] GenerateKeyBytes()
    {
        var key = new byte[32]; // 256 bits
        RandomNumberGenerator.Fill(key);
        return key;
    }
}
