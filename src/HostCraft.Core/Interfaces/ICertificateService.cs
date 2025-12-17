namespace HostCraft.Core.Interfaces;

/// <summary>
/// Service for managing SSL/TLS certificates with Let's Encrypt and other providers.
/// </summary>
public interface ICertificateService
{
    /// <summary>
    /// Request a new SSL certificate for a domain.
    /// </summary>
    Task<Certificate> RequestCertificateAsync(int applicationId, string domain, string email);
    
    /// <summary>
    /// Renew an expiring or expired certificate.
    /// </summary>
    Task<bool> RenewCertificateAsync(int certificateId);
    
    /// <summary>
    /// Check all certificates and auto-renew those expiring soon.
    /// </summary>
    Task<int> AutoRenewExpiringCertificatesAsync();
    
    /// <summary>
    /// Get certificate status and expiration info.
    /// </summary>
    Task<CertificateInfo?> GetCertificateInfoAsync(int certificateId);
    
    /// <summary>
    /// Revoke a certificate.
    /// </summary>
    Task<bool> RevokeCertificateAsync(int certificateId);
    
    /// <summary>
    /// Validate domain ownership for certificate issuance.
    /// </summary>
    Task<bool> ValidateDomainOwnershipAsync(string domain, int serverId);
}

public class CertificateInfo
{
    public int Id { get; set; }
    public string Domain { get; set; } = "";
    public string Provider { get; set; } = "";
    public CertificateStatus Status { get; set; }
    public DateTime? IssuedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public int DaysUntilExpiry { get; set; }
    public bool AutoRenew { get; set; }
    public string? ErrorMessage { get; set; }
}
