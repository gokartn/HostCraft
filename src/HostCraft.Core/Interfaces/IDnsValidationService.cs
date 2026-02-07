using HostCraft.Core.Models;

namespace HostCraft.Core.Interfaces;

/// <summary>
/// Validates that a domain's DNS configuration resolves to the expected server host.
/// </summary>
public interface IDnsValidationService
{
    Task<DomainDnsValidationResult> ValidateAsync(
        string domainHost,
        string expectedServerHost,
        CancellationToken cancellationToken = default);
}
