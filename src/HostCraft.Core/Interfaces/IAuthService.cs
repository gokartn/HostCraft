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
    Task<AuthResult> LoginAsync(string email, string password);

    /// <summary>
    /// Registers a new user account.
    /// </summary>
    Task<AuthResult> RegisterAsync(string email, string password, string? name = null);

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
    Task<AuthResult> RefreshTokenAsync(string refreshToken);
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

    public static AuthResult Failed(string error)
    {
        return new AuthResult
        {
            Success = false,
            Error = error
        };
    }
}
