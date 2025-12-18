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
        var settings = await _context.SystemSettings.FindAsync(1);
        
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

            // Get or create settings (singleton with Id = 1)
            var settings = await _context.SystemSettings.FindAsync(1);
            if (settings == null)
            {
                settings = new SystemSettings
                {
                    Id = 1,
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
                    Message = $"HostCraft domain configured successfully! Your panel is now accessible at {(request.EnableHttps ? "https" : "http")}://{request.Domain}. SSL certificate will be automatically provisioned.",
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

    /// <summary>
    /// Get container logs from HostCraft services (Developer Mode)
    /// </summary>
    [HttpGet("logs")]
    public async Task<ActionResult<ContainerLogsResponse>> GetContainerLogs([FromQuery] int lines = 200)
    {
        try
        {
            string? webLogs = null;
            string? apiLogs = null;
            string? postgresLogs = null;

            // Execute docker logs commands for each container
            var tasks = new List<Task>
            {
                Task.Run(async () =>
                {
                    try
                    {
                        var process = new System.Diagnostics.Process
                        {
                            StartInfo = new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = "/bin/sh",
                                Arguments = $"-c \"docker logs --tail {lines} $(docker ps --filter name=hostcraft_web --format '{{{{.Names}}}}' | head -1)\"",
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                UseShellExecute = false,
                                CreateNoWindow = true
                            }
                        };
                        process.Start();
                        webLogs = await process.StandardOutput.ReadToEndAsync();
                        var errors = await process.StandardError.ReadToEndAsync();
                        if (!string.IsNullOrEmpty(errors))
                        {
                            webLogs += "\n" + errors;
                        }
                        await process.WaitForExitAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to get web container logs");
                        webLogs = $"Failed to get web logs: {ex.Message}";
                    }
                }),
                Task.Run(async () =>
                {
                    try
                    {
                        var process = new System.Diagnostics.Process
                        {
                            StartInfo = new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = "/bin/sh",
                                Arguments = $"-c \"docker logs --tail {lines} $(docker ps --filter name=hostcraft_api --format '{{{{.Names}}}}' | head -1)\"",
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                UseShellExecute = false,
                                CreateNoWindow = true
                            }
                        };
                        process.Start();
                        apiLogs = await process.StandardOutput.ReadToEndAsync();
                        var errors = await process.StandardError.ReadToEndAsync();
                        if (!string.IsNullOrEmpty(errors))
                        {
                            apiLogs += "\n" + errors;
                        }
                        await process.WaitForExitAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to get API container logs");
                        apiLogs = $"Failed to get API logs: {ex.Message}";
                    }
                }),
                Task.Run(async () =>
                {
                    try
                    {
                        var process = new System.Diagnostics.Process
                        {
                            StartInfo = new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = "/bin/sh",
                                Arguments = $"-c \"docker logs --tail {lines} $(docker ps --filter name=hostcraft_postgres --format '{{{{.Names}}}}' | head -1)\"",
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                UseShellExecute = false,
                                CreateNoWindow = true
                            }
                        };
                        process.Start();
                        postgresLogs = await process.StandardOutput.ReadToEndAsync();
                        var errors = await process.StandardError.ReadToEndAsync();
                        if (!string.IsNullOrEmpty(errors))
                        {
                            postgresLogs += "\n" + errors;
                        }
                        await process.WaitForExitAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to get Postgres container logs");
                        postgresLogs = $"Failed to get Postgres logs: {ex.Message}";
                    }
                })
            };

            await Task.WhenAll(tasks);

            return new ContainerLogsResponse
            {
                WebLogs = webLogs ?? "No logs available",
                ApiLogs = apiLogs ?? "No logs available",
                PostgresLogs = postgresLogs ?? "No logs available"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get container logs");
            return StatusCode(500, new { error = $"Failed to get container logs: {ex.Message}" });
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

public record ContainerLogsResponse
{
    public string? WebLogs { get; init; }
    public string? ApiLogs { get; init; }
    public string? PostgresLogs { get; init; }
}
