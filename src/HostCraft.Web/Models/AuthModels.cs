namespace HostCraft.Web.Models;

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

/// <summary>
/// Request model for user login.
/// </summary>
public class LoginRequest
{
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email address")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required")]
    public string Password { get; set; } = string.Empty;
}

/// <summary>
/// Request model for initial setup - matches API SetupRequest.
/// </summary>
public class SetupRequest
{
    [Required(ErrorMessage = "Name is required")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email address")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required")]
    [MinLength(8, ErrorMessage = "Password must be at least 8 characters")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Confirm password is required")]
    [Compare("Password", ErrorMessage = "Passwords do not match")]
    public string ConfirmPassword { get; set; } = string.Empty;

    /// <summary>
    /// Validates password strength (uppercase, lowercase, digit required).
    /// </summary>
    public bool IsPasswordStrong()
    {
        if (string.IsNullOrEmpty(Password) || Password.Length < 8)
            return false;

        return Regex.IsMatch(Password, @"[A-Z]") &&  // Has uppercase
               Regex.IsMatch(Password, @"[a-z]") &&  // Has lowercase
               Regex.IsMatch(Password, @"\d");       // Has digit
    }
}

/// <summary>
/// Request model for token refresh.
/// </summary>
public class RefreshTokenRequest
{
    public string RefreshToken { get; set; } = string.Empty;
}

/// <summary>
/// Response model for login - matches API LoginResponse.
/// </summary>
public class LoginResponse
{
    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;

    [JsonPropertyName("refreshToken")]
    public string RefreshToken { get; set; } = string.Empty;

    [JsonPropertyName("expiresAt")]
    public DateTime ExpiresAt { get; set; }

    [JsonPropertyName("user")]
    public UserDto? User { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>
/// User DTO - matches API UserDto.
/// </summary>
public class UserDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("isAdmin")]
    public bool IsAdmin { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("lastLoginAt")]
    public DateTime? LastLoginAt { get; set; }
}

/// <summary>
/// Response model for setup status check.
/// </summary>
public class SetupStatusResponse
{
    [JsonPropertyName("setupRequired")]
    public bool SetupRequired { get; set; }
}

/// <summary>
/// Result of an authentication operation (internal use).
/// </summary>
public class AuthResult
{
    public bool Success { get; set; }
    public string? Token { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public UserDto? User { get; set; }
    public string? Error { get; set; }

    public static AuthResult Succeeded(string token, string refreshToken, DateTime? expiresAt, UserDto? user)
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
