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

    private const string LegacySecretsPath = "/app/secrets/encryption.key";

    // Marker prefix to identify encrypted values
    private const string EncryptionMarker = "ENC:";

    public EncryptionService(IConfiguration configuration, ILogger<EncryptionService> logger)
    {
        _logger = logger;

        var configuredKey = configuration["Encryption:Key"];
        var configuredKeyPath = NormalizePath(configuration["Encryption:KeyPath"]);

        var keyString = configuredKey;

        if (string.IsNullOrWhiteSpace(keyString))
        {
            keyString = TryLoadKeyFromPath(configuredKeyPath);

            if (string.IsNullOrWhiteSpace(keyString))
            {
                var legacyKey = TryLoadKeyFromPath(LegacySecretsPath);
                if (!string.IsNullOrWhiteSpace(legacyKey))
                {
                    keyString = legacyKey;

                    if (!string.IsNullOrWhiteSpace(configuredKeyPath))
                    {
                        TryPersistKey(configuredKeyPath, legacyKey, logOnSkip: false);
                    }
                }
            }
        }
        else if (!string.IsNullOrWhiteSpace(configuredKeyPath))
        {
            var existingKey = TryLoadKeyFromPath(configuredKeyPath);
            if (!string.IsNullOrWhiteSpace(existingKey) &&
                !string.Equals(existingKey, keyString, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Encryption key mismatch between configuration and stored file at '{configuredKeyPath}'. " +
                    "Update the stored key or remove the file before restarting HostCraft.");
            }

            TryPersistKey(configuredKeyPath, keyString);
        }

        if (string.IsNullOrWhiteSpace(keyString))
        {
            if (string.IsNullOrWhiteSpace(configuredKeyPath))
            {
                throw new InvalidOperationException(
                    "Encryption:Key is required. Provide Encryption:Key or set Encryption:KeyPath so HostCraft can persist one automatically.");
            }

            keyString = GenerateAndPersistKey(configuredKeyPath);
        }

        _key = ConvertKey(keyString);
        _logger.LogInformation("Encryption service initialized successfully");
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

        // All data must be encrypted - fail if it's not
        if (!IsEncrypted(cipherText))
        {
            throw new InvalidOperationException(
                "Data is not encrypted. All sensitive data must be encrypted with ENC: prefix.");
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

    private string? TryLoadKeyFromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var key = File.ReadAllText(path).Trim();

            if (!string.IsNullOrWhiteSpace(key))
            {
                _logger.LogInformation("Loaded encryption key from {KeyPath}", path);
                return key;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read encryption key from {KeyPath}", path);
        }

        return null;
    }

    private void TryPersistKey(string path, string key, bool logOnSkip = true)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(path))
            {
                if (logOnSkip)
                {
                    _logger.LogDebug("Encryption key already exists at {KeyPath}", path);
                }
                return;
            }

            File.WriteAllText(path, key);
            _logger.LogInformation("Persisted encryption key to {KeyPath}", path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist encryption key to {KeyPath}", path);
        }
    }

    private string GenerateAndPersistKey(string path)
    {
        var generatedKey = GenerateKey();

        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(fs);
            writer.Write(generatedKey);
            _logger.LogInformation("Generated new encryption key and persisted it to {KeyPath}", path);
            return generatedKey;
        }
        catch (IOException) when (File.Exists(path))
        {
            _logger.LogInformation("Detected existing encryption key file at {KeyPath} after generation attempt. Reusing stored key.", path);
            return File.ReadAllText(path).Trim();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist generated encryption key to {KeyPath}", path);
            throw new InvalidOperationException("Unable to persist generated encryption key", ex);
        }
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
    }

    private static byte[] ConvertKey(string keyBase64)
    {
        try
        {
            var keyBytes = Convert.FromBase64String(keyBase64);
            if (keyBytes.Length != 32)
            {
                throw new InvalidOperationException("Encryption key must be 32 bytes (256 bits)");
            }

            return keyBytes;
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Encryption:Key must be a valid base64 string of 32 bytes");
        }
    }
}
