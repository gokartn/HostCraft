using HostCraft.Core.Entities;
using HostCraft.Core.Interfaces;
using HostCraft.Core.Interfaces.Repositories;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace HostCraft.Infrastructure.Security;

/// <summary>
/// Manages encrypted secrets (environment variables and private keys).
/// </summary>
public class SecretManager : ISecretManager
{
    private readonly IEnvironmentVariableRepository _environmentVariableRepository;
    private readonly IPrivateKeyRepository _privateKeyRepository;
    private readonly IEncryptionService _encryptionService;
    private readonly ILogger<SecretManager> _logger;

    private const string MaskedValue = "********";

    public SecretManager(
        IEnvironmentVariableRepository environmentVariableRepository,
        IPrivateKeyRepository privateKeyRepository,
        IEncryptionService encryptionService,
        ILogger<SecretManager> logger)
    {
        _environmentVariableRepository = environmentVariableRepository;
        _privateKeyRepository = privateKeyRepository;
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
        var existing = await _environmentVariableRepository.GetByApplicationAndKeyAsync(applicationId, key, cancellationToken);

        var storedValue = isSecret ? _encryptionService.Encrypt(value) : value;

        if (existing != null)
        {
            existing.Value = storedValue;
            existing.IsSecret = isSecret;
            _logger.LogInformation("Updated environment variable {Key} for application {AppId} (IsSecret: {IsSecret})",
                key, applicationId, isSecret);
            await _environmentVariableRepository.UpdateAsync(existing, cancellationToken);
            return existing;
        }

        var newVariable = new EnvironmentVariable
        {
            ApplicationId = applicationId,
            Key = key,
            Value = storedValue,
            IsSecret = isSecret,
            CreatedAt = DateTime.UtcNow
        };

        await _environmentVariableRepository.AddAsync(newVariable, cancellationToken);
        _logger.LogInformation("Created environment variable {Key} for application {AppId} (IsSecret: {IsSecret})",
            key, applicationId, isSecret);
        return newVariable;
    }

    public async Task<EnvironmentVariable?> GetEnvironmentVariableAsync(
        int applicationId,
        string key,
        CancellationToken cancellationToken = default)
    {
        var envVar = await _environmentVariableRepository.GetByApplicationAndKeyAsync(applicationId, key, cancellationToken);

        if (envVar != null && envVar.IsSecret)
        {
            envVar.Value = _encryptionService.IsEncrypted(envVar.Value)
                ? _encryptionService.Decrypt(envVar.Value)
                : envVar.Value;
        }

        return envVar;
    }

    public async Task<IEnumerable<EnvironmentVariable>> GetEnvironmentVariablesAsync(
        int applicationId,
        bool decryptSecrets = true,
        CancellationToken cancellationToken = default)
    {
        var envVars = await _environmentVariableRepository.GetByApplicationAsync(applicationId, cancellationToken);

        foreach (var envVar in envVars)
        {
            if (envVar.IsSecret)
            {
                if (decryptSecrets)
                {
                    envVar.Value = _encryptionService.IsEncrypted(envVar.Value)
                        ? _encryptionService.Decrypt(envVar.Value)
                        : envVar.Value;
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
        var envVar = await _environmentVariableRepository.GetByApplicationAndKeyAsync(applicationId, key, cancellationToken);

        if (envVar == null)
        {
            return false;
        }

        await _environmentVariableRepository.DeleteAsync(envVar, cancellationToken);

        _logger.LogInformation("Deleted environment variable {Key} for application {AppId}", key, applicationId);
        return true;
    }

    public async Task<PrivateKey> SetPrivateKeyAsync(
        string name,
        string keyData,
        string? passphrase = null,
        CancellationToken cancellationToken = default)
    {
        var existing = await _privateKeyRepository.GetByNameAsync(name, cancellationToken);

        var encryptedKeyData = _encryptionService.Encrypt(keyData);
        var encryptedPassphrase = passphrase != null ? _encryptionService.Encrypt(passphrase) : null;

        if (existing != null)
        {
            existing.KeyData = encryptedKeyData;
            existing.Passphrase = encryptedPassphrase;
            _logger.LogInformation("Updated private key {KeyName}", name);
            await _privateKeyRepository.UpdateAsync(existing, cancellationToken);
        }
        else
        {
            var newKey = new PrivateKey
            {
                Name = name,
                KeyData = encryptedKeyData,
                Passphrase = encryptedPassphrase,
                CreatedAt = DateTime.UtcNow
            };
            existing = await _privateKeyRepository.AddAsync(newKey, cancellationToken);
            _logger.LogInformation("Created private key {KeyName}", name);
        }

        return existing;
    }

    public async Task<PrivateKey?> GetPrivateKeyAsync(
        int keyId,
        CancellationToken cancellationToken = default)
    {
        var privateKey = await _privateKeyRepository.GetByIdAsync(keyId, cancellationToken);

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
        var privateKey = await _privateKeyRepository.GetByNameAsync(name, cancellationToken);

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
        var privateKey = await _privateKeyRepository.GetByIdAsync(keyId, cancellationToken);
        if (privateKey == null)
        {
            return false;
        }

        await _privateKeyRepository.DeleteAsync(privateKey, cancellationToken);

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
        var secretEnvVars = await _environmentVariableRepository.GetSecretsAsync(cancellationToken);

        foreach (var envVar in secretEnvVars)
        {
            try
            {
                // Decrypt with old key
                var plainValue = _encryptionService.Decrypt(envVar.Value);

                // Re-encrypt with new key (would need to update encryption service with new key first)
                // This is a placeholder - in production you'd need to coordinate key change
                envVar.Value = _encryptionService.Encrypt(plainValue);
                await _environmentVariableRepository.UpdateAsync(envVar, cancellationToken);
                rotatedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to rotate encryption for environment variable {EnvVarId}", envVar.Id);
            }
        }

        // Rotate private keys
        var privateKeys = await _privateKeyRepository.GetAllAsync(cancellationToken);

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

                await _privateKeyRepository.UpdateAsync(key, cancellationToken);
                rotatedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to rotate encryption for private key {KeyId}", key.Id);
            }
        }

        _logger.LogInformation("Encryption key rotation completed. Rotated {Count} secrets", rotatedCount);
        return rotatedCount;
    }
}
