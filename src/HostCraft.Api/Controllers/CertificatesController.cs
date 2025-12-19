using HostCraft.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HostCraft.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CertificatesController : ControllerBase
{
    private readonly ICertificateService _certificateService;
    private readonly ILogger<CertificatesController> _logger;

    public CertificatesController(ICertificateService certificateService, ILogger<CertificatesController> logger)
    {
        _certificateService = certificateService;
        _logger = logger;
    }

    /// <summary>
    /// Request a new SSL certificate for an application domain.
    /// </summary>
    [HttpPost("request")]
    public async Task<IActionResult> RequestCertificate([FromBody] CertificateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Domain) || string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest(new { error = "Domain and email are required" });
        }

        var certificate = await _certificateService.RequestCertificateAsync(
            request.ApplicationId,
            request.Domain,
            request.Email);

        return Ok(new CertificateDto
        {
            Id = certificate.Id,
            ApplicationId = certificate.ApplicationId,
            Domain = certificate.Domain,
            Provider = certificate.Provider,
            Status = certificate.Status.ToString(),
            IssuedAt = certificate.IssuedAt,
            ExpiresAt = certificate.ExpiresAt,
            AutoRenew = certificate.AutoRenew,
            ErrorMessage = certificate.ErrorMessage
        });
    }

    /// <summary>
    /// Renew an existing certificate.
    /// </summary>
    [HttpPost("{certificateId}/renew")]
    public async Task<IActionResult> RenewCertificate(int certificateId)
    {
        var success = await _certificateService.RenewCertificateAsync(certificateId);
        return Ok(new { success, message = success ? "Renewal initiated" : "Renewal failed" });
    }

    /// <summary>
    /// Auto-renew all expiring certificates.
    /// </summary>
    [HttpPost("auto-renew")]
    public async Task<IActionResult> AutoRenewExpiring()
    {
        var renewedCount = await _certificateService.AutoRenewExpiringCertificatesAsync();
        return Ok(new { renewedCount, message = $"Renewed {renewedCount} certificates" });
    }

    /// <summary>
    /// Get certificate information and status.
    /// </summary>
    [HttpGet("{certificateId}")]
    public async Task<IActionResult> GetCertificateInfo(int certificateId)
    {
        var info = await _certificateService.GetCertificateInfoAsync(certificateId);
        if (info == null)
        {
            return NotFound(new { error = "Certificate not found" });
        }

        return Ok(new CertificateInfoDto
        {
            Id = info.Id,
            Domain = info.Domain,
            Provider = info.Provider,
            Status = info.Status.ToString(),
            IssuedAt = info.IssuedAt,
            ExpiresAt = info.ExpiresAt,
            DaysUntilExpiry = info.DaysUntilExpiry,
            AutoRenew = info.AutoRenew,
            ErrorMessage = info.ErrorMessage
        });
    }

    /// <summary>
    /// Revoke a certificate.
    /// </summary>
    [HttpPost("{certificateId}/revoke")]
    public async Task<IActionResult> RevokeCertificate(int certificateId)
    {
        var success = await _certificateService.RevokeCertificateAsync(certificateId);
        return Ok(new { success, message = success ? "Certificate revoked" : "Revocation failed" });
    }

    /// <summary>
    /// Validate domain ownership before certificate issuance.
    /// </summary>
    [HttpPost("validate-domain")]
    public async Task<IActionResult> ValidateDomain([FromBody] DomainValidationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Domain))
        {
            return BadRequest(new { error = "Domain is required" });
        }

        var isValid = await _certificateService.ValidateDomainOwnershipAsync(request.Domain, request.ServerId);
        return Ok(new { domain = request.Domain, isValid, message = isValid ? "Domain validation passed" : "Domain validation failed" });
    }
}

public record CertificateRequest(int ApplicationId, string Domain, string Email);
public record DomainValidationRequest(string Domain, int ServerId);

public class CertificateDto
{
    public int Id { get; set; }
    public int ApplicationId { get; set; }
    public required string Domain { get; set; }
    public required string Provider { get; set; }
    public required string Status { get; set; }
    public DateTime? IssuedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool AutoRenew { get; set; }
    public string? ErrorMessage { get; set; }
}

public class CertificateInfoDto
{
    public int Id { get; set; }
    public required string Domain { get; set; }
    public required string Provider { get; set; }
    public required string Status { get; set; }
    public DateTime? IssuedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public int DaysUntilExpiry { get; set; }
    public bool AutoRenew { get; set; }
    public string? ErrorMessage { get; set; }
}
