using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using HostCraft.Core.Interfaces;
using HostCraft.Infrastructure.Persistence;

namespace HostCraft.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GitProvidersController : ControllerBase
{
    private readonly HostCraftDbContext _context;
    private readonly IGitProviderService _gitProviderService;
    private readonly ILogger<GitProvidersController> _logger;

    public GitProvidersController(
        HostCraftDbContext context,
        IGitProviderService gitProviderService,
        ILogger<GitProvidersController> logger)
    {
        _context = context;
        _gitProviderService = gitProviderService;
        _logger = logger;
    }

    /// <summary>
    /// Get all connected Git providers for the current user.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<GitProvider>>> GetProviders()
    {
        // TODO: Get actual userId from authentication context
        int userId = 1;
        
        var providers = await _context.GitProviders
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.ConnectedAt)
            .ToListAsync();
        
        // Don't expose sensitive tokens to client
        foreach (var provider in providers)
        {
            provider.AccessToken = "***";
            provider.RefreshToken = null;
        }
        
        return providers;
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
            var redirectUri = $"{Request.Scheme}://{Request.Host}/api/gitproviders/callback";
            var authUrl = await _gitProviderService.GetAuthorizationUrlAsync(type, redirectUri, apiUrl);
            
            return new AuthUrlResponse(authUrl);
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
    public async Task<IActionResult> OAuthCallback(
        [FromQuery] string code,
        [FromQuery] string state)
    {
        try
        {
            // TODO: Validate state parameter for CSRF protection
            // TODO: Get actual userId from authentication context
            int userId = 1;
            
            // Parse state to get provider type and optional API URL
            var stateData = ParseState(state);
            var redirectUri = $"{Request.Scheme}://{Request.Host}/api/gitproviders/callback";
            
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

    private (GitProviderType Type, string? ApiUrl) ParseState(string state)
    {
        // State format: "github" or "gitlab:https://gitlab.example.com"
        var parts = state.Split(':', 2);
        var type = Enum.Parse<GitProviderType>(parts[0], ignoreCase: true);
        var apiUrl = parts.Length > 1 ? parts[1] : null;
        return (type, apiUrl);
    }
}

public record AuthUrlResponse(string AuthUrl);

public record TestConnectionResult
{
    public bool IsValid { get; init; }
    public string Message { get; init; } = string.Empty;
}
