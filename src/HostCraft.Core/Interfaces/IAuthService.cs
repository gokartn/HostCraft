using HostCraft.Core.Entities;

namespace HostCraft.Core.Interfaces;

/// <summary>
/// Authentication service for user management and JWT token generation.
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Authenticates a user and returns a JWT token.
    /// </summary>
    Task<AuthResult> LoginAsync(string email, string password, string? ipAddress = null, string? userAgent = null);

    /// <summary>
    /// Registers a new user account.
    /// </summary>
    Task<AuthResult> RegisterAsync(string email, string password, string? name = null, bool isAdmin = false);

    /// <summary>
    /// Validates a JWT token and returns the user if valid.
    /// </summary>
    Task<User?> ValidateTokenAsync(string token);

    /// <summary>
    /// Changes a user's password.
    /// </summary>
    Task<bool> ChangePasswordAsync(int userId, string currentPassword, string newPassword);

    /// <summary>
    /// Gets a user by their ID.
    /// </summary>
    Task<User?> GetUserByIdAsync(int userId);

    /// <summary>
    /// Gets a user by their email.
    /// </summary>
    Task<User?> GetUserByEmailAsync(string email);

    /// <summary>
    /// Checks if any users exist (for initial setup).
    /// </summary>
    Task<bool> HasAnyUsersAsync();

    /// <summary>
    /// Refreshes an authentication token.
    /// </summary>
    Task<AuthResult> RefreshTokenAsync(string refreshToken, string? ipAddress = null);

    /// <summary>
    /// Revokes a refresh token.
    /// </summary>
    Task<bool> RevokeRefreshTokenAsync(string refreshToken, string? ipAddress = null);

    /// <summary>
    /// Revokes all refresh tokens for a user.
    /// </summary>
    Task<bool> RevokeAllRefreshTokensAsync(int userId, string? ipAddress = null);

    /// <summary>
    /// Enables two-factor authentication for a user.
    /// </summary>
    Task<bool> EnableTwoFactorAsync(int userId, string verificationCode);

    /// <summary>
    /// Disables two-factor authentication for a user.
    /// </summary>
    Task<bool> DisableTwoFactorAsync(int userId, string verificationCode);

    /// <summary>
    /// Gets two-factor authentication setup information for a user.
    /// </summary>
    Task<TwoFactorSetupResult> GetTwoFactorSetupAsync(int userId);

    /// <summary>
    /// Verifies two-factor authentication code during login.
    /// </summary>
    Task<AuthResult> VerifyTwoFactorLoginAsync(string email, string code);

    /// <summary>
    /// Resets a user's password (admin function).
    /// </summary>
    Task<bool> ResetPasswordAsync(string email, string newPassword);

    /// <summary>
    /// Gets audit logs with pagination.
    /// </summary>
    Task<IEnumerable<AuditLog>> GetAuditLogsAsync(int page = 1, int pageSize = 50);

    /// <summary>
    /// Verifies a two-factor authentication code.
    /// </summary>
    Task<bool> VerifyTwoFactorCodeAsync(int userId, string code);

    /// <summary>
    /// Generates recovery codes for a user.
    /// </summary>
    Task<string[]> GenerateRecoveryCodesAsync(int userId);

    /// <summary>
    /// Redeems a recovery code.
    /// </summary>
    Task<bool> RedeemRecoveryCodeAsync(int userId, string code);

    /// <summary>
    /// Confirms a user's email address.
    /// </summary>
    Task<bool> ConfirmEmailAsync(int userId, string token);

    /// <summary>
    /// Initiates password reset for a user.
    /// </summary>
    Task<bool> InitiatePasswordResetAsync(string email);

    /// <summary>
    /// Resets a user's password using a reset token.
    /// </summary>
    Task<bool> ResetPasswordAsync(string email, string token, string newPassword);

    /// <summary>
    /// Locks out a user account.
    /// </summary>
    Task<bool> LockoutUserAsync(int userId, TimeSpan? duration = null);

    /// <summary>
    /// Unlocks a user account.
    /// </summary>
    Task<bool> UnlockUserAsync(int userId);

    /// <summary>
    /// Logs an audit event.
    /// </summary>
    Task LogAuditEventAsync(string eventType, string description, string? userId = null, string? ipAddress = null, string? userAgent = null, bool isSuccess = true, string? additionalData = null);
}

/// <summary>
/// Result of an authentication operation.
/// </summary>
public class AuthResult
{
    public bool Success { get; set; }
    public string? Token { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public User? User { get; set; }
    public string? Error { get; set; }

    public static AuthResult Succeeded(string token, string refreshToken, DateTime expiresAt, User user)
    {
        return new AuthResult
        {
            Success = true,
            Token = token,
            RefreshToken = refreshToken,
            ExpiresAt = expiresAt,
            User = user
        };
    }

    public static AuthResult RegistrationSucceeded(User user)
    {
        return new AuthResult
        {
            Success = true,
            User = user
        };
    }

    public static AuthResult Failed(string error)
    {
        return new AuthResult
        {
            Success = false,
            Error = error
        };
    }
}

/// <summary>
/// Result of a two-factor authentication setup operation.
/// </summary>
public class TwoFactorSetupResult
{
    public bool Success { get; set; }
    public bool IsEnabled { get; set; }
    public string? QrCodeUri { get; set; }
    public string? ManualEntryKey { get; set; }
    public string? Error { get; set; }

    public static TwoFactorSetupResult Succeeded(bool isEnabled, string? qrCodeUri = null, string? manualEntryKey = null)
    {
        return new TwoFactorSetupResult
        {
            Success = true,
            IsEnabled = isEnabled,
            QrCodeUri = qrCodeUri,
            ManualEntryKey = manualEntryKey
        };
    }

    public static TwoFactorSetupResult Failed(string error)
    {
        return new TwoFactorSetupResult
        {
            Success = false,
            Error = error
        };
    }
}
