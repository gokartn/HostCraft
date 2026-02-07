namespace HostCraft.Core.Entities;

/// <summary>
/// SSL/TLS certificate for an application domain.
/// </summary>
public class Certificate
{
    public int Id { get; set; }
    
    public int ApplicationId { get; set; }
    
    public required string Domain { get; set; }
    
    /// <summary>
    /// Certificate provider (Let's Encrypt, ZeroSSL, etc.)
    /// </summary>
    public required string Provider { get; set; } = "Let's Encrypt";
    
    /// <summary>
    /// Certificate status
    /// </summary>
    public CertificateStatus Status { get; set; } = CertificateStatus.Pending;
    
    /// <summary>
    /// When the certificate was issued
    /// </summary>
    public DateTime? IssuedAt { get; set; }
    
    /// <summary>
    /// When the certificate expires
    /// </summary>
    public DateTime? ExpiresAt { get; set; }
    
    /// <summary>
    /// Days before expiry to auto-renew (default: 30)
    /// </summary>
    public int RenewalDays { get; set; } = 30;
    
    /// <summary>
    /// Last renewal attempt
    /// </summary>
    public DateTime? LastRenewalAttempt { get; set; }
    
    /// <summary>
    /// Auto-renew this certificate
    /// </summary>
    public bool AutoRenew { get; set; } = true;
    
    /// <summary>
    /// Certificate serial number
    /// </summary>
    public string? SerialNumber { get; set; }
    
    /// <summary>
    /// Certificate issuer
    /// </summary>
    public string? Issuer { get; set; }
    
    /// <summary>
    /// Error message if certificate issuance failed
    /// </summary>
    public string? ErrorMessage { get; set; }
    
    public DateTime CreatedAt { get; set; }
    
    // Navigation properties
    public Application Application { get; set; } = null!;
}

public enum CertificateStatus
{
    Pending = 0,
    Issuing = 1,
    Active = 2,
    Expiring = 3,
    Expired = 4,
    Failed = 5,
    Renewing = 6
}
