using HostCraft.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Linq;

namespace HostCraft.Api.Controllers;

/// <summary>
/// Authentication endpoints for login, registration, and user management.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IAuthService authService, ILogger<AuthController> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    /// <summary>
    /// Login with email and password.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { error = "Email and password are required" });
        }

        // Get client IP address
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

        var result = await _authService.LoginAsync(request.Email, request.Password, ipAddress);

        if (!result.Success)
        {
            return Unauthorized(new { error = result.Error });
        }

        return Ok(new LoginResponse
        {
            Token = result.Token!,
            RefreshToken = result.RefreshToken!,
            ExpiresAt = result.ExpiresAt!.Value,
            User = new UserDto
            {
                Id = result.User!.Id,
                Email = result.User.Email,
                Name = result.User.Name,
                IsAdmin = result.User.IsAdmin
            }
        });
    }

    /// <summary>
    /// Register a new user account.
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Register([FromBody] RegisterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { error = "Email and password are required" });
        }

        // Check if this is the first user (setup mode)
        var hasUsers = await _authService.HasAnyUsersAsync();

        // If users exist and registration is not from an admin, deny
        // For now, we allow registration if no users exist (initial setup)
        if (hasUsers)
        {
            // Check if caller is admin
            var isAdmin = User?.Claims.FirstOrDefault(c => c.Type == "isAdmin")?.Value == "true";
            if (!isAdmin)
            {
                return Forbid();
            }
        }

        var result = await _authService.RegisterAsync(request.Email, request.Password, request.Name);

        if (!result.Success)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(new LoginResponse
        {
            Token = result.Token!,
            RefreshToken = result.RefreshToken!,
            ExpiresAt = result.ExpiresAt!.Value,
            User = new UserDto
            {
                Id = result.User!.Id,
                Email = result.User.Email,
                Name = result.User.Name,
                IsAdmin = result.User.IsAdmin
            }
        });
    }

    /// <summary>
    /// Get current user information.
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UserDto>> GetCurrentUser()
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                       ?? User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized();
        }

        var user = await _authService.GetUserByIdAsync(userId);
        if (user == null)
        {
            return NotFound();
        }

        return Ok(new UserDto
        {
            Id = user.Id,
            Email = user.Email,
            Name = user.Name,
            IsAdmin = user.IsAdmin
        });
    }

    /// <summary>
    /// Change password for current user.
    /// </summary>
    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                       ?? User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.CurrentPassword) || string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return BadRequest(new { error = "Current and new password are required" });
        }

        var success = await _authService.ChangePasswordAsync(userId, request.CurrentPassword, request.NewPassword);

        if (!success)
        {
            return BadRequest(new { error = "Invalid current password or new password too short" });
        }

        return Ok(new { message = "Password changed successfully" });
    }

    /// <summary>
    /// Check if initial setup is required (no users exist).
    /// </summary>
    [HttpGet("setup-required")]
    [AllowAnonymous]
    public async Task<ActionResult<SetupStatusResponse>> CheckSetupRequired()
    {
        var hasUsers = await _authService.HasAnyUsersAsync();
        return Ok(new SetupStatusResponse { SetupRequired = !hasUsers });
    }

    /// <summary>
    /// Complete initial setup by creating the first admin user.
    /// </summary>
    [HttpPost("setup")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> CompleteSetup([FromBody] SetupRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password) || string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { error = "Email, password, and name are required" });
        }

        // Verify setup is still required
        var hasUsers = await _authService.HasAnyUsersAsync();
        if (hasUsers)
        {
            return BadRequest(new { error = "Setup has already been completed" });
        }

        // Validate password strength
        if (request.Password.Length < 8 ||
            !request.Password.Any(char.IsUpper) ||
            !request.Password.Any(char.IsLower) ||
            !request.Password.Any(char.IsDigit))
        {
            return BadRequest(new { error = "Password must be at least 8 characters with uppercase, lowercase, and numbers" });
        }

        var result = await _authService.RegisterAsync(request.Email, request.Password, request.Name, isAdmin: true);

        if (!result.Success)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(new LoginResponse
        {
            Token = result.Token!,
            RefreshToken = result.RefreshToken!,
            ExpiresAt = result.ExpiresAt!.Value,
            User = new UserDto
            {
                Id = result.User!.Id,
                Email = result.User.Email,
                Name = result.User.Name,
                IsAdmin = result.User.IsAdmin
            }
        });
    }

    /// <summary>
    /// Logout by revoking the refresh token.
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return BadRequest(new { error = "Refresh token is required" });
        }

        var success = await _authService.RevokeRefreshTokenAsync(request.RefreshToken);
        if (!success)
        {
            return BadRequest(new { error = "Invalid refresh token" });
        }

        return Ok(new { message = "Logged out successfully" });
    }

    /// <summary>
    /// Get 2FA setup information for current user.
    /// </summary>
    [HttpGet("2fa/setup")]
    [Authorize]
    public async Task<ActionResult<TwoFactorSetupResponse>> GetTwoFactorSetup()
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                       ?? User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized();
        }

        var result = await _authService.GetTwoFactorSetupAsync(userId);
        if (!result.Success)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(new TwoFactorSetupResponse
        {
            IsEnabled = result.IsEnabled,
            QrCodeUri = result.QrCodeUri,
            ManualEntryKey = result.ManualEntryKey
        });
    }

    /// <summary>
    /// Enable 2FA for current user.
    /// </summary>
    [HttpPost("2fa/enable")]
    [Authorize]
    public async Task<IActionResult> EnableTwoFactor([FromBody] TwoFactorCodeRequest request)
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                       ?? User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return BadRequest(new { error = "Verification code is required" });
        }

        var success = await _authService.EnableTwoFactorAsync(userId, request.Code);
        if (!success)
        {
            return BadRequest(new { error = "Invalid verification code" });
        }

        return Ok(new { message = "Two-factor authentication enabled successfully" });
    }

    /// <summary>
    /// Disable 2FA for current user.
    /// </summary>
    [HttpPost("2fa/disable")]
    [Authorize]
    public async Task<IActionResult> DisableTwoFactor([FromBody] TwoFactorCodeRequest request)
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                       ?? User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return BadRequest(new { error = "Verification code is required" });
        }

        var success = await _authService.DisableTwoFactorAsync(userId, request.Code);
        if (!success)
        {
            return BadRequest(new { error = "Invalid verification code" });
        }

        return Ok(new { message = "Two-factor authentication disabled successfully" });
    }

    /// <summary>
    /// Verify 2FA code during login.
    /// </summary>
    [HttpPost("2fa/verify")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> VerifyTwoFactor([FromBody] TwoFactorLoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Code))
        {
            return BadRequest(new { error = "Email and verification code are required" });
        }

        var result = await _authService.VerifyTwoFactorLoginAsync(request.Email, request.Code);

        if (!result.Success)
        {
            return Unauthorized(new { error = result.Error });
        }

        return Ok(new LoginResponse
        {
            Token = result.Token!,
            RefreshToken = result.RefreshToken!,
            ExpiresAt = result.ExpiresAt!.Value,
            User = new UserDto
            {
                Id = result.User!.Id,
                Email = result.User.Email,
                Name = result.User.Name,
                IsAdmin = result.User.IsAdmin
            }
        });
    }

    /// <summary>
    /// Reset password for a user (admin only).
    /// </summary>
    [HttpPost("reset-password")]
    [Authorize]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        var isAdmin = User?.Claims.FirstOrDefault(c => c.Type == "isAdmin")?.Value == "true";
        if (!isAdmin)
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return BadRequest(new { error = "Email and new password are required" });
        }

        var success = await _authService.ResetPasswordAsync(request.Email, request.NewPassword);
        if (!success)
        {
            return BadRequest(new { error = "User not found or password reset failed" });
        }

        return Ok(new { message = "Password reset successfully" });
    }

    /// <summary>
    /// Get audit logs (admin only).
    /// </summary>
    [HttpGet("audit-logs")]
    [Authorize]
    public async Task<ActionResult<IEnumerable<AuditLogDto>>> GetAuditLogs([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var isAdmin = User?.Claims.FirstOrDefault(c => c.Type == "isAdmin")?.Value == "true";
        if (!isAdmin)
        {
            return Forbid();
        }

        var logs = await _authService.GetAuditLogsAsync(page, pageSize);
        return Ok(logs.Select(log => new AuditLogDto
        {
            Id = log.Id,
            UserId = !string.IsNullOrEmpty(log.UserId) && int.TryParse(log.UserId, out var userId) ? userId : null,
            EventType = log.EventType,
            Description = log.Description,
            IpAddress = log.IpAddress,
            UserAgent = log.UserAgent,
            IsSuccess = log.IsSuccess,
            Timestamp = log.Timestamp
        }));
    }
}

// Request/Response DTOs
public record LoginRequest(string Email, string Password);
public record RegisterRequest(string Email, string Password, string? Name);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public record RefreshTokenRequest(string RefreshToken);
public record SetupRequest(string Email, string Password, string Name);

public class LoginResponse
{
    public required string Token { get; set; }
    public required string RefreshToken { get; set; }
    public DateTime ExpiresAt { get; set; }
    public required UserDto User { get; set; }
}

public class UserDto
{
    public int Id { get; set; }
    public required string Email { get; set; }
    public string? Name { get; set; }
    public bool IsAdmin { get; set; }
}

public class SetupStatusResponse
{
    public bool SetupRequired { get; set; }
}

public record TwoFactorCodeRequest(string Code);
public record TwoFactorLoginRequest(string Email, string Code);
public record ResetPasswordRequest(string Email, string NewPassword);

public class TwoFactorSetupResponse
{
    public bool IsEnabled { get; set; }
    public string? QrCodeUri { get; set; }
    public string? ManualEntryKey { get; set; }
}

public class AuditLogDto
{
    public int Id { get; set; }
    public int? UserId { get; set; }
    public required string EventType { get; set; }
    public required string Description { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public bool IsSuccess { get; set; }
    public DateTime Timestamp { get; set; }
}
