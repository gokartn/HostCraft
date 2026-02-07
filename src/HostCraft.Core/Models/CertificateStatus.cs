namespace HostCraft.Core.Models;

/// <summary>
/// Certificate status information for a domain
/// </summary>
public class DomainCertificateStatus
{
    public required string Domain { get; set; }
    
    /// <summary>
    /// Status: valid, pending, rate_limited, acme_error, config_error, unknown, no_logs
    /// </summary>
    public required string Status { get; set; }
    
    public string? ErrorMessage { get; set; }
    
    public DateTime LastChecked { get; set; }
    
    public DateTime? RetryAfter { get; set; }
    
    /// <summary>
    /// Get user-friendly status display
    /// </summary>
    public string GetDisplayStatus() => Status switch
    {
        "valid" => "✅ Valid Certificate",
        "pending" => "⏳ Certificate Pending",
        "rate_limited" => "🚫 Rate Limited",
        "acme_error" => "❌ Certificate Error",
        "config_error" => "⚠️ Configuration Error",
        "no_logs" => "❓ Status Unknown",
        _ => "❓ Unknown Status"
    };
    
    /// <summary>
    /// Get CSS class for status indicator
    /// </summary>
    public string GetStatusClass() => Status switch
    {
        "valid" => "cert-status-valid",
        "pending" => "cert-status-pending",
        "rate_limited" => "cert-status-rate-limited",
        "acme_error" => "cert-status-error",
        "config_error" => "cert-status-error",
        _ => "cert-status-unknown"
    };
}