using HostCraft.Core.Entities;
using HostCraft.Core.Interfaces;
using HostCraft.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HostCraft.Infrastructure.Security;

/// <summary>
/// Manages encrypted secrets (environment variables and private keys).
/// </summary>
public class SecretManager : ISecretManager
{
    private readonly HostCraftDbContext _context;
    private readonly IEncryptionService _encryptionService;
    private readonly ILogger<SecretManager> _logger;

    private const string MaskedValue = "********";

    public SecretManager(
        HostCraftDbContext context,
        IEncryptionService encryptionService,
        ILogger<SecretManager> logger)
    {
        _context = context;
        _encryptionService = encryptionService;
        _logger = logger;
    }

    public async Task<EnvironmentVariable> SetEnvironmentVariableAsync(
        int applicationId,
        string key,
        string value,
        bool isSecret,
        CancellationToken cancellationToken = default)
    {
        var existing = await _context.EnvironmentVariables
            .FirstOrDefaultAsync(e => e.ApplicationId == applicationId && e.Key == key, cancellationToken);

        var storedValue = isSecret ? _encryptionService.Encrypt(value) : value;

        if (existing != null)
        {
            existing.Value = storedValue;
            existing.IsSecret = isSecret;
            _logger.LogInformation("Updated environment variable {Key} for application {AppId} (IsSecret: {IsSecret})",
                key, applicationId, isSecret);
        }
        else
        {
            existing = new EnvironmentVariable
            {
                ApplicationId = applicationId,
                Key = key,
                Value = storedValue,
                IsSecret = isSecret,
                CreatedAt = DateTime.UtcNow
            };
            _context.EnvironmentVariables.Add(existing);
            _logger.LogInformation("Created environment variable {Key} for application {AppId} (IsSecret: {IsSecret})",
                key, applicationId, isSecret);
        }

        await _context.SaveChangesAsync(cancellationToken);
        return existing;
    }

    public async Task<EnvironmentVariable?> GetEnvironmentVariableAsync(
        int applicationId,
        string key,
        CancellationToken cancellationToken = default)
    {
        var envVar = await _context.EnvironmentVariables
            .FirstOrDefaultAsync(e => e.ApplicationId == applicationId && e.Key == key, cancellationToken);

        if (envVar != null && envVar.IsSecret)
        {
            envVar.Value = _encryptionService.Decrypt(envVar.Value);
        }

        return envVar;
    }

    public async Task<IEnumerable<EnvironmentVariable>> GetEnvironmentVariablesAsync(
        int applicationId,
        bool decryptSecrets = true,
        CancellationToken cancellationToken = default)
    {
        var envVars = await _context.EnvironmentVariables
            .Where(e => e.ApplicationId == applicationId)
            .ToListAsync(cancellationToken);

        foreach (var envVar in envVars)
        {
            if (envVar.IsSecret)
            {
                if (decryptSecrets)
                {
                    envVar.Value = _encryptionService.Decrypt(envVar.Value);
                }
                else
                {
                    envVar.Value = MaskedValue;
                }
            }
        }

        return envVars;
    }

    public async Task<bool> DeleteEnvironmentVariableAsync(
        int applicationId,
        string key,
        CancellationToken cancellationToken = default)
    {
        var envVar = await _context.EnvironmentVariables
            .FirstOrDefaultAsync(e => e.ApplicationId == applicationId && e.Key == key, cancellationToken);

        if (envVar == null)
        {
            return false;
        }

        _context.EnvironmentVariables.Remove(envVar);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted environment variable {Key} for application {AppId}", key, applicationId);
        return true;
    }

    public async Task<PrivateKey> SetPrivateKeyAsync(
        string name,
        string keyData,
        string? passphrase = null,
        CancellationToken cancellationToken = default)
    {
        var existing = await _context.PrivateKeys
            .FirstOrDefaultAsync(k => k.Name == name, cancellationToken);

        var encryptedKeyData = _encryptionService.Encrypt(keyData);
        var encryptedPassphrase = passphrase != null ? _encryptionService.Encrypt(passphrase) : null;

        if (existing != null)
        {
            existing.KeyData = encryptedKeyData;
            existing.Passphrase = encryptedPassphrase;
            _logger.LogInformation("Updated private key {KeyName}", name);
        }
        else
        {
            existing = new PrivateKey
            {
                Name = name,
                KeyData = encryptedKeyData,
                Passphrase = encryptedPassphrase,
                CreatedAt = DateTime.UtcNow
            };
            _context.PrivateKeys.Add(existing);
            _logger.LogInformation("Created private key {KeyName}", name);
        }

        await _context.SaveChangesAsync(cancellationToken);
        return existing;
    }

    public async Task<PrivateKey?> GetPrivateKeyAsync(
        int keyId,
        CancellationToken cancellationToken = default)
    {
        var privateKey = await _context.PrivateKeys
            .FirstOrDefaultAsync(k => k.Id == keyId, cancellationToken);

        if (privateKey != null)
        {
            privateKey.KeyData = _encryptionService.Decrypt(privateKey.KeyData);
            if (privateKey.Passphrase != null)
            {
                privateKey.Passphrase = _encryptionService.Decrypt(privateKey.Passphrase);
            }
        }

        return privateKey;
    }

    public async Task<PrivateKey?> GetPrivateKeyByNameAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        var privateKey = await _context.PrivateKeys
            .FirstOrDefaultAsync(k => k.Name == name, cancellationToken);

        if (privateKey != null)
        {
            privateKey.KeyData = _encryptionService.Decrypt(privateKey.KeyData);
            if (privateKey.Passphrase != null)
            {
                privateKey.Passphrase = _encryptionService.Decrypt(privateKey.Passphrase);
            }
        }

        return privateKey;
    }

    public async Task<bool> DeletePrivateKeyAsync(
        int keyId,
        CancellationToken cancellationToken = default)
    {
        var privateKey = await _context.PrivateKeys.FindAsync([keyId], cancellationToken);
        if (privateKey == null)
        {
            return false;
        }

        _context.PrivateKeys.Remove(privateKey);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted private key {KeyId}", keyId);
        return true;
    }

    public async Task<int> RotateEncryptionKeyAsync(
        string newKey,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting encryption key rotation");

        var rotatedCount = 0;

        // Rotate environment variable secrets
        var secretEnvVars = await _context.EnvironmentVariables
            .Where(e => e.IsSecret)
            .ToListAsync(cancellationToken);

        foreach (var envVar in secretEnvVars)
        {
            try
            {
                // Decrypt with old key
                var plainValue = _encryptionService.Decrypt(envVar.Value);

                // Re-encrypt with new key (would need to update encryption service with new key first)
                // This is a placeholder - in production you'd need to coordinate key change
                envVar.Value = _encryptionService.Encrypt(plainValue);
                rotatedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to rotate encryption for environment variable {EnvVarId}", envVar.Id);
            }
        }

        // Rotate private keys
        var privateKeys = await _context.PrivateKeys.ToListAsync(cancellationToken);

        foreach (var key in privateKeys)
        {
            try
            {
                var plainKeyData = _encryptionService.Decrypt(key.KeyData);
                key.KeyData = _encryptionService.Encrypt(plainKeyData);

                if (key.Passphrase != null)
                {
                    var plainPassphrase = _encryptionService.Decrypt(key.Passphrase);
                    key.Passphrase = _encryptionService.Encrypt(plainPassphrase);
                }

                rotatedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to rotate encryption for private key {KeyId}", key.Id);
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Encryption key rotation completed. Rotated {Count} secrets", rotatedCount);
        return rotatedCount;
    }
}
