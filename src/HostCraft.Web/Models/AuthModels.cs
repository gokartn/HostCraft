namespace HostCraft.Web.Models;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Request model for user login.
/// </summary>
public class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

/// <summary>
/// Request model for initial setup.
/// </summary>
public class SetupRequestDto
{
    [Required]
    public string Name { get; set; } = "";

    [Required]
    [EmailAddress]
    public string Email { get; set; } = "";

    [Required]
    [MinLength(1)]
    public string Password { get; set; } = "";

    [Required]
    [Compare("Password")]
    public string ConfirmPassword { get; set; } = "";
}

/// <summary>
/// Request model for token refresh.
/// </summary>
public class RefreshTokenRequest
{
    public string RefreshToken { get; set; } = string.Empty;
}

/// <summary>
/// Response model for authentication operations.
/// </summary>
public class AuthResponse
{
    public bool Success { get; set; }
    public string? Token { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public User? User { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// User model for the web application.
/// </summary>
public class User
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? Name { get; set; }
    public bool IsAdmin { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Response model for setup status check.
/// </summary>
public class SetupStatusResponse
{
    public bool SetupRequired { get; set; }
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

    public static AuthResult Succeeded(string token, string refreshToken, DateTime? expiresAt, User user)
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