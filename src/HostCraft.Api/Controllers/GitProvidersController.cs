using HostCraft.Api.Models.GitProviders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using HostCraft.Core.Interfaces;
using HostCraft.Core.Interfaces.Repositories;
using HostCraft.Infrastructure.Services;

namespace HostCraft.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class GitProvidersController : ControllerBase
{
    private readonly IGitProviderRepository _gitProviderRepository;
    private readonly IGitProviderService _gitProviderService;
    private readonly IGitOAuthService _gitOAuthService;
    private readonly ILogger<GitProvidersController> _logger;

    public GitProvidersController(
        IGitProviderRepository gitProviderRepository,
        IGitProviderService gitProviderService,
        IGitOAuthService gitOAuthService,
        ILogger<GitProvidersController> logger)
    {
        _gitProviderRepository = gitProviderRepository;
        _gitProviderService = gitProviderService;
        _gitOAuthService = gitOAuthService;
        _logger = logger;
    }

    /// <summary>
    /// Get all connected Git providers for the current user.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<GitProviderDto>>> GetProviders()
    {
        // Get userId from authentication context (defaults to 1 for single-user mode)
        int userId = GetCurrentUserId();

        var providers = await _gitProviderRepository.GetByUserAsync(userId);

        // Return as DTOs without sensitive data
        var dtos = providers.Select(p => new GitProviderDto(
            p.Id,
            p.Name,
            p.Type.ToString(), // Convert enum to string for JSON serialization
            p.Username,
            p.IsActive
        ));

        return Ok(dtos);
    }

    /// <summary>
    /// Get OAuth authorization URL for connecting a Git provider.
    /// </summary>
    [HttpGet("auth-url")]
    public async Task<ActionResult<AuthUrlResponse>> GetAuthUrl(
        [FromQuery] GitProviderType type,
        [FromQuery] string? apiUrl = null)
    {
        try
        {
            var redirectUri = await _gitOAuthService.GetPublicCallbackUrlAsync(Request);
            var authUrl = await _gitProviderService.GetAuthorizationUrlAsync(type, redirectUri, apiUrl);

            return new AuthUrlResponse(authUrl);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not configured"))
        {
            _logger.LogWarning("OAuth not configured for {Type}: {Message}", type, ex.Message);
            return BadRequest(new { error = $"{type} OAuth credentials not configured. Please set {type}:ClientId and {type}:ClientSecret in environment variables or appsettings." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate auth URL for {Type}", type);
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// OAuth callback endpoint - exchanges code for token.
    /// </summary>
    [HttpGet("callback")]
    [AllowAnonymous]
    public async Task<IActionResult> OAuthCallback(
        [FromQuery] string code,
        [FromQuery] string state)
    {
        try
        {
            // Validate state parameter for CSRF protection
            if (string.IsNullOrEmpty(state))
            {
                return BadRequest(new { error = "Missing state parameter" });
            }

            // Get userId from authentication context (defaults to 1 for single-user mode)
            int userId = GetCurrentUserId();

            // Parse state to get provider type and optional API URL
            var stateData = _gitOAuthService.ParseState(state);

            // IMPORTANT: Use the same public callback URL that was sent to the OAuth provider
            // This must match exactly or the token exchange will fail
            var redirectUri = await _gitOAuthService.GetPublicCallbackUrlAsync(Request);

            var provider = await _gitProviderService.ConnectProviderAsync(
                stateData.Type,
                code,
                redirectUri,
                userId,
                stateData.ApiUrl);
            
            // Redirect back to UI with success
            return Redirect($"/settings/git-providers?connected={provider.Id}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OAuth callback failed");
            return Redirect($"/settings/git-providers?error={Uri.EscapeDataString(ex.Message)}");
        }
    }

    /// <summary>
    /// Get repositories for a connected Git provider.
    /// </summary>
    [HttpGet("{id}/repositories")]
    public async Task<ActionResult<List<GitRepository>>> GetRepositories(int id)
    {
        try
        {
            var repositories = await _gitProviderService.GetRepositoriesAsync(id);
            return repositories;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch repositories for provider {ProviderId}", id);
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get branches for a repository.
    /// </summary>
    [HttpGet("{id}/repositories/{owner}/{repo}/branches")]
    public async Task<ActionResult<List<string>>> GetBranches(int id, string owner, string repo)
    {
        try
        {
            var branches = await _gitProviderService.GetBranchesAsync(id, owner, repo);
            return branches;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch branches for {Owner}/{Repo}", owner, repo);
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Test connection to a Git provider.
    /// </summary>
    [HttpPost("{id}/test")]
    public async Task<ActionResult<TestConnectionResult>> TestConnection(int id)
    {
        try
        {
            var isValid = await _gitProviderService.TestConnectionAsync(id);
            return new TestConnectionResult
            {
                IsValid = isValid,
                Message = isValid ? "Connection successful" : "Connection failed"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to test connection for provider {ProviderId}", id);
            return new TestConnectionResult
            {
                IsValid = false,
                Message = ex.Message
            };
        }
    }

    /// <summary>
    /// Disconnect and delete a Git provider.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DisconnectProvider(int id)
    {
        try
        {
            var success = await _gitProviderService.DisconnectProviderAsync(id);
            if (!success)
            {
                return NotFound();
            }
            
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to disconnect provider {ProviderId}", id);
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Gets the current user ID from the authentication context.
    /// Returns 1 for single-user mode (no authentication required).
    /// When authentication is implemented, this will extract the user ID from JWT claims.
    /// </summary>
    private int GetCurrentUserId()
    {
        // Check if we have an authenticated user with claims
        if (User?.Identity?.IsAuthenticated == true)
        {
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                           ?? User.FindFirst("sub")?.Value
                           ?? User.FindFirst("userId")?.Value;

            if (!string.IsNullOrEmpty(userIdClaim) && int.TryParse(userIdClaim, out var userId))
            {
                return userId;
            }
        }

        // Default to user 1 for single-user mode (no authentication)
        return 1;
    }
}
