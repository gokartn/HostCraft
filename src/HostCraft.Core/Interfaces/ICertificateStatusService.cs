using HostCraft.Core.Entities;
using HostCraft.Core.Models;

namespace HostCraft.Core.Interfaces;

/// <summary>
/// Interface for checking certificate status from Traefik and Let's Encrypt
/// </summary>
public interface ICertificateStatusService
{
    /// <summary>
    /// Get certificate status for a single domain
    /// </summary>
    Task<DomainCertificateStatus> GetCertificateStatusAsync(Server server, string domain);
    
    /// <summary>
    /// Get certificate status for multiple domains
    /// </summary>
    Task<List<DomainCertificateStatus>> GetAllCertificateStatusesAsync(Server server, List<string> domains);
}