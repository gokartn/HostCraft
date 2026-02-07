using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using HostCraft.Core.Interfaces;
using HostCraft.Infrastructure.Persistence;

namespace HostCraft.Infrastructure.Services;

/// <summary>
/// Service for managing JWT settings stored in the database.
/// Ensures consistent JWT configuration across API and Web services.
/// Uses a static in-memory cache with double-check locking to avoid repeated DB calls.
/// </summary>
public class JwtSettingsService : IJwtSettingsService
{
    private readonly HostCraftDbContext _dbContext;
    private readonly IEncryptionService _encryptionService;
    private readonly ILogger<JwtSettingsService> _logger;

    // Static in-memory cache - JWT settings are immutable once loaded, so process-level caching is sufficient
    private static JwtSettings? _cachedSettings;
    private static readonly SemaphoreSlim _lock = new(1, 1);

    public JwtSettingsService(
        HostCraftDbContext dbContext,
        IEncryptionService encryptionService,
        ILogger<JwtSettingsService> logger)
    {
        _dbContext = dbContext;
        _encryptionService = encryptionService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<JwtSettings> GetJwtSettingsAsync()
    {
        // Return cached settings if available
        if (_cachedSettings != null)
        {
            return _cachedSettings;
        }

        await _lock.WaitAsync();
        try
        {
            // Double-check after acquiring lock
            if (_cachedSettings != null)
            {
                return _cachedSettings;
            }

            await EnsureJwtSettingsExistAsync();

            // Always use FindAsync(1) for singleton pattern
            var systemSettings = await _dbContext.SystemSettings.FindAsync(1);
            if (systemSettings == null || string.IsNullOrEmpty(systemSettings.JwtSecret))
            {
                throw new InvalidOperationException("JWT settings not found in database after initialization");
            }

            // Decrypt the JWT secret
            string decryptedSecret;
            try
            {
                decryptedSecret = _encryptionService.Decrypt(systemSettings.JwtSecret);
            }
            catch
            {
                // If decryption fails, the secret might be stored in plain text (legacy)
                // or encryption key changed - regenerate
                _logger.LogWarning("Failed to decrypt JWT secret, regenerating...");
                decryptedSecret = GenerateJwtSecret();
                systemSettings.JwtSecret = _encryptionService.Encrypt(decryptedSecret);
                systemSettings.UpdatedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();
            }

            _cachedSettings = new JwtSettings
            {
                Secret = decryptedSecret,
                Issuer = systemSettings.JwtIssuer,
                Audience = systemSettings.JwtAudience,
                ExpirationMinutes = systemSettings.JwtExpirationMinutes
            };

            _logger.LogInformation("JWT settings loaded from database");
            return _cachedSettings;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task EnsureJwtSettingsExistAsync()
    {
        // Always use Id = 1 for singleton pattern - consistent with SystemSettingsController
        var systemSettings = await _dbContext.SystemSettings.FindAsync(1);

        if (systemSettings == null)
        {
            // Create new system settings with JWT configuration
            // IMPORTANT: Use Id = 1 for singleton pattern
            var jwtSecret = GenerateJwtSecret();
            systemSettings = new Core.Entities.SystemSettings
            {
                Id = 1, // Singleton pattern - always use Id = 1
                JwtSecret = _encryptionService.Encrypt(jwtSecret),
                JwtIssuer = "HostCraft",
                JwtAudience = "HostCraft",
                JwtExpirationMinutes = 60,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            try
            {
                _dbContext.SystemSettings.Add(systemSettings);
                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("Created new JWT settings in database with Id=1");
            }
            catch (DbUpdateException)
            {
                // Race condition: another replica already created SystemSettings with Id=1
                // Detach our entity and reload from database
                _dbContext.Entry(systemSettings).State = EntityState.Detached;
                systemSettings = await _dbContext.SystemSettings.FindAsync(1);
                if (systemSettings == null || string.IsNullOrEmpty(systemSettings.JwtSecret))
                {
                    throw new InvalidOperationException("JWT settings not found after race condition recovery");
                }
                _logger.LogInformation("JWT settings already created by another replica, loaded from database");
            }
        }
        else if (string.IsNullOrEmpty(systemSettings.JwtSecret))
        {
            // Existing settings but no JWT secret - add one
            var jwtSecret = GenerateJwtSecret();
            systemSettings.JwtSecret = _encryptionService.Encrypt(jwtSecret);
            systemSettings.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Added JWT secret to existing system settings");
        }
    }

    /// <summary>
    /// Generates a cryptographically secure JWT secret.
    /// </summary>
    private static string GenerateJwtSecret()
    {
        var bytes = new byte[64]; // 512 bits
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }
}
