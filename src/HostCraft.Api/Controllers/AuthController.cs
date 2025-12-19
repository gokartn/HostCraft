using HostCraft.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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

        var result = await _authService.LoginAsync(request.Email, request.Password);

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
    /// Refresh authentication token.
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> RefreshToken([FromBody] RefreshTokenRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return BadRequest(new { error = "Refresh token is required" });
        }

        var result = await _authService.RefreshTokenAsync(request.RefreshToken);

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
}

// Request/Response DTOs
public record LoginRequest(string Email, string Password);
public record RegisterRequest(string Email, string Password, string? Name);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public record RefreshTokenRequest(string RefreshToken);

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
