using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HostCraft.Core.Entities;
using HostCraft.Core.Interfaces;
using HostCraft.Infrastructure.Persistence;

namespace HostCraft.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SystemSettingsController : ControllerBase
{
    private readonly HostCraftDbContext _context;
    private readonly IProxyService _proxyService;
    private readonly ILogger<SystemSettingsController> _logger;

    public SystemSettingsController(
        HostCraftDbContext context,
        IProxyService proxyService,
        ILogger<SystemSettingsController> logger)
    {
        _context = context;
        _proxyService = proxyService;
        _logger = logger;
    }

    /// <summary>
    /// Get current HostCraft system settings
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<SystemSettingsDto>> GetSettings()
    {
        var settings = await _context.SystemSettings.FirstOrDefaultAsync();
        
        if (settings == null)
        {
            // Return default settings
            return new SystemSettingsDto
            {
                HostCraftDomain = null,
                HostCraftEnableHttps = true,
                HostCraftLetsEncryptEmail = null,
                CertificateStatus = null
            };
        }

        return new SystemSettingsDto
        {
            HostCraftDomain = settings.HostCraftDomain,
            HostCraftEnableHttps = settings.HostCraftEnableHttps,
            HostCraftLetsEncryptEmail = settings.HostCraftLetsEncryptEmail,
            CertificateStatus = settings.CertificateStatus,
            ConfiguredAt = settings.ConfiguredAt,
            ProxyUpdatedAt = settings.ProxyUpdatedAt
        };
    }

    /// <summary>
    /// Update HostCraft domain and SSL configuration
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ConfigureHostCraftResponse>> ConfigureHostCraft(
        [FromBody] ConfigureHostCraftRequest request)
    {
        try
        {
            // Validate
            if (string.IsNullOrWhiteSpace(request.Domain))
            {
                return BadRequest(new ConfigureHostCraftResponse
                {
                    Success = false,
                    Message = "Domain is required"
                });
            }

            if (request.EnableHttps && string.IsNullOrWhiteSpace(request.LetsEncryptEmail))
            {
                return BadRequest(new ConfigureHostCraftResponse
                {
                    Success = false,
                    Message = "Email is required when HTTPS is enabled"
                });
            }

            // Get or create settings
            var settings = await _context.SystemSettings.FirstOrDefaultAsync();
            if (settings == null)
            {
                settings = new SystemSettings
                {
                    CreatedAt = DateTime.UtcNow
                };
                _context.SystemSettings.Add(settings);
            }

            // Update settings
            settings.HostCraftDomain = request.Domain;
            settings.HostCraftEnableHttps = request.EnableHttps;
            settings.HostCraftLetsEncryptEmail = request.LetsEncryptEmail;
            settings.ConfiguredAt = DateTime.UtcNow;
            settings.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            // Configure proxy to route domain to HostCraft web service
            try
            {
                await _proxyService.ConfigureHostCraftDomainAsync(
                    request.Domain,
                    request.EnableHttps,
                    request.LetsEncryptEmail);

                settings.ProxyUpdatedAt = DateTime.UtcNow;
                settings.CertificateStatus = request.EnableHttps ? "Requesting..." : "Disabled";
                await _context.SaveChangesAsync();

                return new ConfigureHostCraftResponse
                {
                    Success = true,
                    Message = $"HostCraft domain configured successfully! Access your panel at {(request.EnableHttps ? "https" : "http")}://{request.Domain}",
                    Domain = request.Domain,
                    HttpsEnabled = request.EnableHttps
                };
            }
            catch (Exception proxyEx)
            {
                _logger.LogError(proxyEx, "Failed to configure proxy for HostCraft domain");
                
                // Settings saved but proxy config failed
                return StatusCode(500, new ConfigureHostCraftResponse
                {
                    Success = false,
                    Message = $"Settings saved but proxy configuration failed: {proxyEx.Message}. Please check your proxy service."
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to configure HostCraft domain");
            return StatusCode(500, new ConfigureHostCraftResponse
            {
                Success = false,
                Message = $"Configuration failed: {ex.Message}"
            });
        }
    }
}

public record ConfigureHostCraftRequest(
    string Domain,
    bool EnableHttps,
    string? LetsEncryptEmail);

public record ConfigureHostCraftResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? Domain { get; init; }
    public bool HttpsEnabled { get; init; }
}

public record SystemSettingsDto
{
    public string? HostCraftDomain { get; init; }
    public bool HostCraftEnableHttps { get; init; }
    public string? HostCraftLetsEncryptEmail { get; init; }
    public string? CertificateStatus { get; init; }
    public DateTime? ConfiguredAt { get; init; }
    public DateTime? ProxyUpdatedAt { get; init; }
}
