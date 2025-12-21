using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HostCraft.Core.Entities;
using HostCraft.Core.Interfaces;
using HostCraft.Infrastructure.Persistence;

namespace HostCraft.Api.Controllers;

[ApiController]
[Route("api/applications/{applicationId}/[controller]")]
[Authorize]
public class DomainsController : ControllerBase
{
    private readonly HostCraftDbContext _context;
    private readonly ICertificateService _certificateService;
    private readonly IProxyService _proxyService;
    private readonly ILogger<DomainsController> _logger;

    public DomainsController(
        HostCraftDbContext context,
        ICertificateService certificateService,
        IProxyService proxyService,
        ILogger<DomainsController> logger)
    {
        _context = context;
        _certificateService = certificateService;
        _proxyService = proxyService;
        _logger = logger;
    }

    /// <summary>
    /// Configure domain and SSL for an application.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<DomainConfigResponse>> ConfigureDomain(
        int applicationId,
        [FromBody] ConfigureDomainRequest request)
    {
        var application = await _context.Applications
            .Include(a => a.Server)
            .Include(a => a.Certificates)
            .FirstOrDefaultAsync(a => a.Id == applicationId);

        if (application == null)
            return NotFound();

        try
        {
            // Update application domain settings
            application.Domain = request.Domain;
            application.AdditionalDomains = request.AdditionalDomains;
            application.EnableHttps = request.EnableHttps;
            application.ForceHttps = request.ForceHttps;
            application.LetsEncryptEmail = request.LetsEncryptEmail ?? application.Server.DefaultLetsEncryptEmail;

            await _context.SaveChangesAsync();

            // Request SSL certificate if HTTPS is enabled
            Certificate? certificate = null;
            if (request.EnableHttps && !string.IsNullOrEmpty(request.Domain))
            {
                var email = application.LetsEncryptEmail ?? "admin@hostcraft.local";
                certificate = await _certificateService.RequestCertificateAsync(
                    applicationId,
                    request.Domain,
                    email);
            }

            // Reconfigure proxy
            await _proxyService.ConfigureApplicationAsync(application);

            return new DomainConfigResponse
            {
                Success = true,
                Message = "Domain configured successfully",
                Domain = application.Domain,
                HttpsEnabled = application.EnableHttps,
                CertificateStatus = certificate?.Status.ToString()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to configure domain for application {AppId}", applicationId);
            return BadRequest(new DomainConfigResponse
            {
                Success = false,
                Message = ex.Message
            });
        }
    }

    /// <summary>
    /// Get certificate information for an application.
    /// </summary>
    [HttpGet("certificates")]
    public async Task<ActionResult<List<CertificateInfo>>> GetCertificates(int applicationId)
    {
        var certificates = await _context.Set<Certificate>()
            .Where(c => c.ApplicationId == applicationId)
            .Select(c => new CertificateInfo
            {
                Id = c.Id,
                Domain = c.Domain,
                Provider = c.Provider,
                Status = c.Status,
                IssuedAt = c.IssuedAt,
                ExpiresAt = c.ExpiresAt,
                DaysUntilExpiry = c.ExpiresAt.HasValue 
                    ? (int)(c.ExpiresAt.Value - DateTime.UtcNow).TotalDays 
                    : 0,
                AutoRenew = c.AutoRenew,
                ErrorMessage = c.ErrorMessage
            })
            .ToListAsync();

        return certificates;
    }

    /// <summary>
    /// Manually renew a certificate.
    /// </summary>
    [HttpPost("certificates/{certificateId}/renew")]
    public async Task<ActionResult<RenewCertificateResponse>> RenewCertificate(
        int applicationId,
        int certificateId)
    {
        try
        {
            var success = await _certificateService.RenewCertificateAsync(certificateId);
            return new RenewCertificateResponse
            {
                Success = success,
                Message = success ? "Certificate renewed successfully" : "Failed to renew certificate"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to renew certificate {CertId}", certificateId);
            return BadRequest(new RenewCertificateResponse
            {
                Success = false,
                Message = ex.Message
            });
        }
    }

    /// <summary>
    /// Delete a certificate.
    /// </summary>
    [HttpDelete("certificates/{certificateId}")]
    public async Task<IActionResult> DeleteCertificate(int applicationId, int certificateId)
    {
        var certificate = await _context.Set<Certificate>()
            .FirstOrDefaultAsync(c => c.Id == certificateId && c.ApplicationId == applicationId);

        if (certificate == null)
            return NotFound();

        _context.Set<Certificate>().Remove(certificate);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}

public record ConfigureDomainRequest(
    string? Domain,
    string? AdditionalDomains,
    bool EnableHttps = true,
    bool ForceHttps = true,
    string? LetsEncryptEmail = null);

public record DomainConfigResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? Domain { get; init; }
    public bool HttpsEnabled { get; init; }
    public string? CertificateStatus { get; init; }
}

public record RenewCertificateResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
}
