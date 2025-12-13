using HostCraft.Core.Enums;

namespace HostCraft.Core.Entities;

/// <summary>
/// Records health check results for applications and servers.
/// </summary>
public class HealthCheck
{
    public int Id { get; set; }
    
    public int? ApplicationId { get; set; }
    
    public int? ServerId { get; set; }
    
    public HealthStatus Status { get; set; }
    
    public int ResponseTimeMs { get; set; }
    
    public string? StatusCode { get; set; }
    
    public string? ErrorMessage { get; set; }
    
    public DateTime CheckedAt { get; set; }
    
    // Navigation properties
    public Application? Application { get; set; }
    
    public Server? Server { get; set; }
}
