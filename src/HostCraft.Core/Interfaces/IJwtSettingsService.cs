namespace HostCraft.Core.Interfaces;

/// <summary>
/// Service for managing JWT settings stored in the database.
/// Ensures consistent JWT configuration across API and Web services.
/// </summary>
public interface IJwtSettingsService
{
    /// <summary>
    /// Gets the JWT settings from the database.
    /// Creates default settings with a generated secret if none exist.
    /// </summary>
    Task<JwtSettings> GetJwtSettingsAsync();

    /// <summary>
    /// Ensures JWT settings exist in the database.
    /// Called during application startup.
    /// </summary>
    Task EnsureJwtSettingsExistAsync();
}

/// <summary>
/// JWT configuration settings retrieved from the database.
/// </summary>
public class JwtSettings
{
    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "HostCraft";
    public string Audience { get; set; } = "HostCraft";
    public int ExpirationMinutes { get; set; } = 60;
}
