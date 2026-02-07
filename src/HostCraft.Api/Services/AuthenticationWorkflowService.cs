using System.Security.Claims;
using HostCraft.Api.Models.Auth;
using HostCraft.Core.Entities;
using HostCraft.Core.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Linq;

namespace HostCraft.Api.Services;

public class AuthenticationWorkflowService : IAuthenticationWorkflowService
{
    private readonly IAuthService _authService;
    private readonly ILogger<AuthenticationWorkflowService> _logger;

    public AuthenticationWorkflowService(IAuthService authService, ILogger<AuthenticationWorkflowService> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    public async Task<AuthActionResult<LoginResponse>> LoginAsync(LoginRequest request, string? ipAddress, string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return AuthActionResult<LoginResponse>.Fail(StatusCodes.Status400BadRequest, "Email and password are required");
        }

        var authResult = await _authService.LoginAsync(request.Email, request.Password, ipAddress, userAgent);

        if (!authResult.Success || authResult.Token is null || authResult.RefreshToken is null || !authResult.ExpiresAt.HasValue || authResult.User is null)
        {
            return AuthActionResult<LoginResponse>.Fail(StatusCodes.Status401Unauthorized, authResult.Error ?? "Invalid credentials");
        }

        return AuthActionResult<LoginResponse>.Ok(MapLoginResponse(authResult));
    }

    public async Task<AuthActionResult<LoginResponse>> RefreshAsync(RefreshTokenRequest request, string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return AuthActionResult<LoginResponse>.Fail(StatusCodes.Status400BadRequest, "Refresh token is required");
        }

        var authResult = await _authService.RefreshTokenAsync(request.RefreshToken, ipAddress);

        if (!authResult.Success || authResult.Token is null || authResult.RefreshToken is null || !authResult.ExpiresAt.HasValue || authResult.User is null)
        {
            return AuthActionResult<LoginResponse>.Fail(StatusCodes.Status401Unauthorized, authResult.Error ?? "Invalid refresh token");
        }

        return AuthActionResult<LoginResponse>.Ok(MapLoginResponse(authResult));
    }

    public async Task<AuthActionResult<LoginResponse>> RegisterAsync(RegisterRequest request, ClaimsPrincipal? caller)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return AuthActionResult<LoginResponse>.Fail(StatusCodes.Status400BadRequest, "Email and password are required");
        }

        var hasUsers = await _authService.HasAnyUsersAsync();
        if (hasUsers && !IsAdmin(caller))
        {
            return AuthActionResult<LoginResponse>.Fail(StatusCodes.Status403Forbidden, "Only admins can register new users");
        }

        var authResult = await _authService.RegisterAsync(request.Email, request.Password, request.Name);

        if (!authResult.Success)
        {
            return AuthActionResult<LoginResponse>.Fail(StatusCodes.Status400BadRequest, authResult.Error ?? "Registration failed");
        }

        if (authResult.Token is null || authResult.RefreshToken is null || !authResult.ExpiresAt.HasValue || authResult.User is null)
        {
            var loginResult = await _authService.LoginAsync(request.Email, request.Password);
            if (!loginResult.Success || loginResult.Token is null || loginResult.RefreshToken is null || !loginResult.ExpiresAt.HasValue || loginResult.User is null)
            {
                return AuthActionResult<LoginResponse>.Fail(StatusCodes.Status400BadRequest, loginResult.Error ?? "Registration succeeded but login failed");
            }

            return AuthActionResult<LoginResponse>.Ok(MapLoginResponse(loginResult), StatusCodes.Status201Created);
        }

        return AuthActionResult<LoginResponse>.Ok(MapLoginResponse(authResult));
    }

    public async Task<AuthActionResult<UserDto>> GetCurrentUserAsync(ClaimsPrincipal principal)
    {
        if (!TryGetUserId(principal, out var userId))
        {
            return AuthActionResult<UserDto>.Fail(StatusCodes.Status401Unauthorized, "User not authenticated");
        }

        var user = await _authService.GetUserByIdAsync(userId);
        if (user == null)
        {
            return AuthActionResult<UserDto>.Fail(StatusCodes.Status404NotFound, "User not found");
        }

        return AuthActionResult<UserDto>.Ok(MapUserDto(user));
    }

    public async Task<AuthActionResult> ChangePasswordAsync(ClaimsPrincipal principal, ChangePasswordRequest request)
    {
        if (!TryGetUserId(principal, out var userId))
        {
            return AuthActionResult.Fail(StatusCodes.Status401Unauthorized, "User not authenticated");
        }

        if (string.IsNullOrWhiteSpace(request.CurrentPassword) || string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return AuthActionResult.Fail(StatusCodes.Status400BadRequest, "Current and new password are required");
        }

        var success = await _authService.ChangePasswordAsync(userId, request.CurrentPassword, request.NewPassword);
        if (!success)
        {
            return AuthActionResult.Fail(StatusCodes.Status400BadRequest, "Invalid current password or new password too short");
        }

        return AuthActionResult.Ok();
    }

    public async Task<AuthActionResult<SetupStatusResponse>> GetSetupStatusAsync()
    {
        try
        {
            var hasUsers = await _authService.HasAnyUsersAsync();
            _logger.LogInformation("Setup check: hasUsers={HasUsers}, setupRequired={SetupRequired}", hasUsers, !hasUsers);
            return AuthActionResult<SetupStatusResponse>.Ok(new SetupStatusResponse { SetupRequired = !hasUsers });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking setup status, assuming setup is required");
            return AuthActionResult<SetupStatusResponse>.Ok(new SetupStatusResponse { SetupRequired = true });
        }
    }

    public async Task<AuthActionResult<LoginResponse>> CompleteSetupAsync(SetupRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password) || string.IsNullOrWhiteSpace(request.Name))
        {
            return AuthActionResult<LoginResponse>.Fail(StatusCodes.Status400BadRequest, "Email, password, and name are required");
        }

        var hasUsers = await _authService.HasAnyUsersAsync();
        if (hasUsers)
        {
            return AuthActionResult<LoginResponse>.Fail(StatusCodes.Status400BadRequest, "Setup has already been completed");
        }

        var passwordError = ValidateSetupPassword(request.Password);
        if (passwordError is not null)
        {
            return AuthActionResult<LoginResponse>.Fail(StatusCodes.Status400BadRequest, passwordError);
        }

        var authResult = await _authService.RegisterAsync(request.Email, request.Password, request.Name, isAdmin: true);
        if (!authResult.Success || authResult.Token is null || authResult.RefreshToken is null || !authResult.ExpiresAt.HasValue || authResult.User is null)
        {
            return AuthActionResult<LoginResponse>.Fail(StatusCodes.Status400BadRequest, authResult.Error ?? "Setup failed");
        }

        return AuthActionResult<LoginResponse>.Ok(MapLoginResponse(authResult));
    }

    public async Task<AuthActionResult> LogoutAsync(RefreshTokenRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return AuthActionResult.Fail(StatusCodes.Status400BadRequest, "Refresh token is required");
        }

        var success = await _authService.RevokeRefreshTokenAsync(request.RefreshToken);
        if (!success)
        {
            return AuthActionResult.Fail(StatusCodes.Status400BadRequest, "Invalid refresh token");
        }

        return AuthActionResult.Ok();
    }

    public async Task<AuthActionResult<TwoFactorSetupResponse>> GetTwoFactorSetupAsync(ClaimsPrincipal principal)
    {
        if (!TryGetUserId(principal, out var userId))
        {
            return AuthActionResult<TwoFactorSetupResponse>.Fail(StatusCodes.Status401Unauthorized, "User not authenticated");
        }

        var result = await _authService.GetTwoFactorSetupAsync(userId);
        if (!result.Success)
        {
            return AuthActionResult<TwoFactorSetupResponse>.Fail(StatusCodes.Status400BadRequest, result.Error ?? "Failed to load two-factor setup");
        }

        return AuthActionResult<TwoFactorSetupResponse>.Ok(new TwoFactorSetupResponse
        {
            IsEnabled = result.IsEnabled,
            QrCodeUri = result.QrCodeUri,
            ManualEntryKey = result.ManualEntryKey
        });
    }

    public async Task<AuthActionResult> EnableTwoFactorAsync(ClaimsPrincipal principal, TwoFactorCodeRequest request)
    {
        if (!TryGetUserId(principal, out var userId))
        {
            return AuthActionResult.Fail(StatusCodes.Status401Unauthorized, "User not authenticated");
        }

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return AuthActionResult.Fail(StatusCodes.Status400BadRequest, "Verification code is required");
        }

        var success = await _authService.EnableTwoFactorAsync(userId, request.Code);
        if (!success)
        {
            return AuthActionResult.Fail(StatusCodes.Status400BadRequest, "Invalid verification code");
        }

        return AuthActionResult.Ok();
    }

    public async Task<AuthActionResult> DisableTwoFactorAsync(ClaimsPrincipal principal, TwoFactorCodeRequest request)
    {
        if (!TryGetUserId(principal, out var userId))
        {
            return AuthActionResult.Fail(StatusCodes.Status401Unauthorized, "User not authenticated");
        }

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return AuthActionResult.Fail(StatusCodes.Status400BadRequest, "Verification code is required");
        }

        var success = await _authService.DisableTwoFactorAsync(userId, request.Code);
        if (!success)
        {
            return AuthActionResult.Fail(StatusCodes.Status400BadRequest, "Invalid verification code");
        }

        return AuthActionResult.Ok();
    }

    public async Task<AuthActionResult<LoginResponse>> VerifyTwoFactorLoginAsync(TwoFactorLoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Code))
        {
            return AuthActionResult<LoginResponse>.Fail(StatusCodes.Status400BadRequest, "Email and verification code are required");
        }

        var result = await _authService.VerifyTwoFactorLoginAsync(request.Email, request.Code);

        if (!result.Success || result.Token is null || result.RefreshToken is null || !result.ExpiresAt.HasValue || result.User is null)
        {
            return AuthActionResult<LoginResponse>.Fail(StatusCodes.Status401Unauthorized, result.Error ?? "Invalid verification code");
        }

        return AuthActionResult<LoginResponse>.Ok(MapLoginResponse(result));
    }

    public async Task<AuthActionResult> ResetPasswordAsync(ClaimsPrincipal principal, ResetPasswordRequest request)
    {
        if (!IsAdmin(principal))
        {
            return AuthActionResult.Fail(StatusCodes.Status403Forbidden, "Only admins can reset passwords");
        }

        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return AuthActionResult.Fail(StatusCodes.Status400BadRequest, "Email and new password are required");
        }

        var success = await _authService.ResetPasswordAsync(request.Email, request.NewPassword);
        if (!success)
        {
            return AuthActionResult.Fail(StatusCodes.Status400BadRequest, "User not found or password reset failed");
        }

        return AuthActionResult.Ok();
    }

    public async Task<AuthActionResult<IEnumerable<AuditLogDto>>> GetAuditLogsAsync(ClaimsPrincipal principal, int page, int pageSize)
    {
        if (!IsAdmin(principal))
        {
            return AuthActionResult<IEnumerable<AuditLogDto>>.Fail(StatusCodes.Status403Forbidden, "Admin access required");
        }

        var logs = await _authService.GetAuditLogsAsync(page, pageSize);
        var mapped = logs.Select(MapAuditLogDto);
        return AuthActionResult<IEnumerable<AuditLogDto>>.Ok(mapped);
    }

    private static bool TryGetUserId(ClaimsPrincipal? principal, out int userId)
    {
        userId = 0;
        var userIdClaim = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                       ?? principal?.FindFirst("sub")?.Value;

        return !string.IsNullOrEmpty(userIdClaim) && int.TryParse(userIdClaim, out userId);
    }

    private static bool IsAdmin(ClaimsPrincipal? principal)
    {
        return principal?.Claims.FirstOrDefault(c => c.Type == "isAdmin")?.Value == "true";
    }

    private static LoginResponse MapLoginResponse(AuthResult result)
    {
        return new LoginResponse
        {
            Token = result.Token!,
            RefreshToken = result.RefreshToken!,
            ExpiresAt = result.ExpiresAt!.Value,
            User = MapUserDto(result.User!)
        };
    }

    private static UserDto MapUserDto(User user)
    {
        return new UserDto
        {
            Id = user.Id,
            Email = user.Email,
            Name = user.Name,
            IsAdmin = user.IsAdmin
        };
    }

    private static AuditLogDto MapAuditLogDto(HostCraft.Core.Entities.AuditLog log)
    {
        return new AuditLogDto
        {
            Id = log.Id,
            UserId = !string.IsNullOrEmpty(log.UserId) && int.TryParse(log.UserId, out var parsedUserId) ? parsedUserId : null,
            EventType = log.EventType,
            Description = log.Description,
            IpAddress = log.IpAddress,
            UserAgent = log.UserAgent,
            IsSuccess = log.IsSuccess,
            Timestamp = log.Timestamp
        };
    }

    private static string? ValidateSetupPassword(string password)
    {
        if (password.Length < 8 ||
            !password.Any(char.IsUpper) ||
            !password.Any(char.IsLower) ||
            !password.Any(char.IsDigit))
        {
            return "Password must be at least 8 characters with uppercase, lowercase, and numbers";
        }

        return null;
    }
}
