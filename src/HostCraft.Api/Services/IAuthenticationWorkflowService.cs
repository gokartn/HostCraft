using System.Security.Claims;
using HostCraft.Api.Models.Auth;

namespace HostCraft.Api.Services;

public interface IAuthenticationWorkflowService
{
    Task<AuthActionResult<LoginResponse>> LoginAsync(LoginRequest request, string? ipAddress, string? userAgent);
    Task<AuthActionResult<LoginResponse>> RefreshAsync(RefreshTokenRequest request, string? ipAddress);
    Task<AuthActionResult<LoginResponse>> RegisterAsync(RegisterRequest request, ClaimsPrincipal? caller);
    Task<AuthActionResult<UserDto>> GetCurrentUserAsync(ClaimsPrincipal principal);
    Task<AuthActionResult> ChangePasswordAsync(ClaimsPrincipal principal, ChangePasswordRequest request);
    Task<AuthActionResult<SetupStatusResponse>> GetSetupStatusAsync();
    Task<AuthActionResult<LoginResponse>> CompleteSetupAsync(SetupRequest request);
    Task<AuthActionResult> LogoutAsync(RefreshTokenRequest request);
    Task<AuthActionResult<TwoFactorSetupResponse>> GetTwoFactorSetupAsync(ClaimsPrincipal principal);
    Task<AuthActionResult> EnableTwoFactorAsync(ClaimsPrincipal principal, TwoFactorCodeRequest request);
    Task<AuthActionResult> DisableTwoFactorAsync(ClaimsPrincipal principal, TwoFactorCodeRequest request);
    Task<AuthActionResult<LoginResponse>> VerifyTwoFactorLoginAsync(TwoFactorLoginRequest request);
    Task<AuthActionResult> ResetPasswordAsync(ClaimsPrincipal principal, ResetPasswordRequest request);
    Task<AuthActionResult<IEnumerable<AuditLogDto>>> GetAuditLogsAsync(ClaimsPrincipal principal, int page, int pageSize);
}
