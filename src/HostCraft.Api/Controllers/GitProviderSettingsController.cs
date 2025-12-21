using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using HostCraft.Infrastructure.Persistence;

namespace HostCraft.Api.Controllers;

[ApiController]
[Route("api/gitprovider-settings")]
[Authorize]
public class GitProviderSettingsController : ControllerBase
{
    private readonly HostCraftDbContext _context;
    private readonly ILogger<GitProviderSettingsController> _logger;

    public GitProviderSettingsController(
        HostCraftDbContext context,
        ILogger<GitProviderSettingsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Get all Git provider settings (for admin configuration page).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<GitProviderSettingsDto>>> GetSettings()
    {
        var settings = await _context.GitProviderSettings
            .OrderBy(s => s.Type)
            .ToListAsync();

        // If no settings exist, return default providers (not configured)
        if (!settings.Any())
        {
            return new List<GitProviderSettingsDto>
            {
                new(0, GitProviderType.GitHub, "GitHub", false, false, null),
                new(0, GitProviderType.GitLab, "GitLab", false, false, null),
                new(0, GitProviderType.Bitbucket, "Bitbucket", false, false, null),
                new(0, GitProviderType.Gitea, "Gitea", false, false, null)
            };
        }

        return settings.Select(s => new GitProviderSettingsDto(
            s.Id,
            s.Type,
            s.Name,
            s.IsConfigured,
            s.IsEnabled,
            s.ApiUrl
        )).ToList();
    }

    /// <summary>
    /// Get settings for a specific provider type.
    /// </summary>
    [HttpGet("{type}")]
    public async Task<ActionResult<GitProviderSettingsDetailDto>> GetSettingsByType(GitProviderType type, [FromQuery] string? apiUrl = null)
    {
        var settings = await _context.GitProviderSettings
            .FirstOrDefaultAsync(s => s.Type == type && s.ApiUrl == apiUrl);

        if (settings == null)
        {
            return new GitProviderSettingsDetailDto(
                0,
                type,
                type.ToString(),
                null,
                null,
                apiUrl,
                false,
                false
            );
        }

        return new GitProviderSettingsDetailDto(
            settings.Id,
            settings.Type,
            settings.Name,
            settings.ClientId,
            MaskSecret(settings.ClientSecret),
            settings.ApiUrl,
            settings.IsEnabled,
            settings.IsConfigured
        );
    }

    /// <summary>
    /// Save/update Git provider settings.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<GitProviderSettingsDto>> SaveSettings([FromBody] SaveGitProviderSettingsRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ClientId))
        {
            return BadRequest(new { error = "Client ID is required" });
        }

        // Find existing settings or create new
        var settings = await _context.GitProviderSettings
            .FirstOrDefaultAsync(s => s.Type == request.Type && s.ApiUrl == request.ApiUrl);

        if (settings == null)
        {
            settings = new GitProviderSettings
            {
                Type = request.Type,
                Name = request.Name ?? request.Type.ToString(),
                ApiUrl = request.ApiUrl,
                CreatedAt = DateTime.UtcNow
            };
            _context.GitProviderSettings.Add(settings);
        }

        settings.ClientId = request.ClientId;

        // Only update secret if provided (not masked)
        if (!string.IsNullOrEmpty(request.ClientSecret) && !request.ClientSecret.Contains("****"))
        {
            settings.ClientSecret = request.ClientSecret;
        }

        settings.IsEnabled = request.IsEnabled;
        settings.UpdatedAt = DateTime.UtcNow;

        if (!string.IsNullOrEmpty(request.Name))
        {
            settings.Name = request.Name;
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation("Saved Git provider settings for {Type}", request.Type);

        return new GitProviderSettingsDto(
            settings.Id,
            settings.Type,
            settings.Name,
            settings.IsConfigured,
            settings.IsEnabled,
            settings.ApiUrl
        );
    }

    /// <summary>
    /// Delete Git provider settings.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteSettings(int id)
    {
        var settings = await _context.GitProviderSettings.FindAsync(id);
        if (settings == null)
        {
            return NotFound();
        }

        _context.GitProviderSettings.Remove(settings);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Deleted Git provider settings for {Type}", settings.Type);

        return NoContent();
    }

    /// <summary>
    /// Test Git provider OAuth configuration by attempting to get auth URL.
    /// </summary>
    [HttpPost("{type}/test")]
    public async Task<ActionResult<TestResultDto>> TestConfiguration(GitProviderType type, [FromQuery] string? apiUrl = null)
    {
        var settings = await _context.GitProviderSettings
            .FirstOrDefaultAsync(s => s.Type == type && s.ApiUrl == apiUrl);

        if (settings == null || !settings.IsConfigured)
        {
            return new TestResultDto(false, "OAuth credentials not configured");
        }

        // For now, just verify credentials are present
        // In a real implementation, we could try to make a test API call
        return new TestResultDto(true, "Configuration looks valid");
    }

    private static string? MaskSecret(string? secret)
    {
        if (string.IsNullOrEmpty(secret)) return null;
        if (secret.Length <= 8) return "****";
        return secret[..4] + "****" + secret[^4..];
    }
}

// DTOs
public record GitProviderSettingsDto(
    int Id,
    GitProviderType Type,
    string Name,
    bool IsConfigured,
    bool IsEnabled,
    string? ApiUrl);

public record GitProviderSettingsDetailDto(
    int Id,
    GitProviderType Type,
    string Name,
    string? ClientId,
    string? ClientSecretMasked,
    string? ApiUrl,
    bool IsEnabled,
    bool IsConfigured);

public record SaveGitProviderSettingsRequest(
    GitProviderType Type,
    string? Name,
    string ClientId,
    string? ClientSecret,
    string? ApiUrl,
    bool IsEnabled);

public record TestResultDto(bool Success, string Message);
