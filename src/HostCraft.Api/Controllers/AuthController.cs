using HostCraft.Api.Models.Auth;
using HostCraft.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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
    private readonly IAuthenticationWorkflowService _authWorkflow;

    public AuthController(IAuthenticationWorkflowService authWorkflow)
    {
        _authWorkflow = authWorkflow;
    }

    /// <summary>
    /// Login with email and password.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request)
    {
        var result = await _authWorkflow.LoginAsync(request, GetIpAddress(), GetUserAgent());
        return ToActionResult(result);
    }

    /// <summary>
    /// Refresh an access token using a valid refresh token.
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Refresh([FromBody] RefreshTokenRequest request)
    {
        var result = await _authWorkflow.RefreshAsync(request, GetIpAddress());
        return ToActionResult(result);
    }

    /// <summary>
    /// Register a new user account.
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Register([FromBody] RegisterRequest request)
    {
        var result = await _authWorkflow.RegisterAsync(request, User);
        return ToActionResult(result);
    }

    /// <summary>
    /// Get current user information.
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UserDto>> GetCurrentUser()
    {
        var result = await _authWorkflow.GetCurrentUserAsync(User);
        return ToActionResult(result);
    }

    /// <summary>
    /// Change password for current user.
    /// </summary>
    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var result = await _authWorkflow.ChangePasswordAsync(User, request);
        return ToActionResult(result);
    }

    /// <summary>
    /// Check if initial setup is required (no users exist).
    /// </summary>
    [HttpGet("setup-required")]
    [AllowAnonymous]
    public async Task<ActionResult<SetupStatusResponse>> CheckSetupRequired()
    {
        var result = await _authWorkflow.GetSetupStatusAsync();
        return ToActionResult(result);
    }

    /// <summary>
    /// Complete initial setup by creating the first admin user.
    /// </summary>
    [HttpPost("setup")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> CompleteSetup([FromBody] SetupRequest request)
    {
        var result = await _authWorkflow.CompleteSetupAsync(request);
        return ToActionResult(result);
    }

    /// <summary>
    /// Logout by revoking the refresh token.
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest request)
    {
        var result = await _authWorkflow.LogoutAsync(request);
        return ToActionResult(result);
    }

    /// <summary>
    /// Get 2FA setup information for current user.
    /// </summary>
    [HttpGet("2fa/setup")]
    [Authorize]
    public async Task<ActionResult<TwoFactorSetupResponse>> GetTwoFactorSetup()
    {
        var result = await _authWorkflow.GetTwoFactorSetupAsync(User);
        return ToActionResult(result);
    }

    /// <summary>
    /// Enable 2FA for current user.
    /// </summary>
    [HttpPost("2fa/enable")]
    [Authorize]
    public async Task<IActionResult> EnableTwoFactor([FromBody] TwoFactorCodeRequest request)
    {
        var result = await _authWorkflow.EnableTwoFactorAsync(User, request);
        return ToActionResult(result);
    }

    /// <summary>
    /// Disable 2FA for current user.
    /// </summary>
    [HttpPost("2fa/disable")]
    [Authorize]
    public async Task<IActionResult> DisableTwoFactor([FromBody] TwoFactorCodeRequest request)
    {
        var result = await _authWorkflow.DisableTwoFactorAsync(User, request);
        return ToActionResult(result);
    }

    /// <summary>
    /// Verify 2FA code during login.
    /// </summary>
    [HttpPost("2fa/verify")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> VerifyTwoFactor([FromBody] TwoFactorLoginRequest request)
    {
        var result = await _authWorkflow.VerifyTwoFactorLoginAsync(request);
        return ToActionResult(result);
    }

    /// <summary>
    /// Reset password for a user (admin only).
    /// </summary>
    [HttpPost("reset-password")]
    [Authorize]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        var result = await _authWorkflow.ResetPasswordAsync(User, request);
        return ToActionResult(result);
    }

    /// <summary>
    /// Get audit logs (admin only).
    /// </summary>
    [HttpGet("audit-logs")]
    [Authorize]
    public async Task<ActionResult<IEnumerable<AuditLogDto>>> GetAuditLogs([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var result = await _authWorkflow.GetAuditLogsAsync(User, page, pageSize);
        return ToActionResult(result);
    }

    private string? GetIpAddress() => HttpContext.Connection.RemoteIpAddress?.ToString();

    private string? GetUserAgent() => Request.Headers["User-Agent"].FirstOrDefault();

    private ActionResult ToActionResult(AuthActionResult result)
    {
        if (result.Success)
        {
            if (result.StatusCode == StatusCodes.Status204NoContent)
            {
                return StatusCode(StatusCodes.Status204NoContent);
            }

            return StatusCode(result.StatusCode);
        }

        return StatusCode(result.StatusCode, new { error = result.Error ?? "Request failed" });
    }

    private ActionResult<T> ToActionResult<T>(AuthActionResult<T> result)
    {
        if (result.Success)
        {
            if (result.StatusCode == StatusCodes.Status204NoContent)
            {
                return StatusCode(StatusCodes.Status204NoContent);
            }

            return StatusCode(result.StatusCode, result.Data);
        }

        return StatusCode(result.StatusCode, new { error = result.Error ?? "Request failed" });
    }
}
