using HostCraft.Api.Models.Domains;
using HostCraft.Api.Models.Shared;
using HostCraft.Core.Entities;

namespace HostCraft.Api.Services;

public interface IDomainsWorkflowService
{
    Task<ApiActionResult<PagedResult<DomainDto>>> GetDomainsAsync(int applicationId, bool paged, int page, int pageSize, CancellationToken cancellationToken);
    Task<ApiActionResult<DomainDto>> GetDomainAsync(int applicationId, int id, CancellationToken cancellationToken);
    Task<ApiActionResult<DomainDto>> CreateDomainAsync(int applicationId, CreateDomainRequest request, CancellationToken cancellationToken);
    Task<ApiActionResult<DomainDto>> UpdateDomainAsync(int applicationId, int id, UpdateDomainRequest request, CancellationToken cancellationToken);
    Task<ApiActionResult> DeleteDomainAsync(int applicationId, int id, CancellationToken cancellationToken);
    Task<ApiActionResult<DnsValidationResult>> ValidateDnsAsync(int applicationId, int id, CancellationToken cancellationToken);
    Task<ApiActionResult<IEnumerable<DnsValidationResult>>> ValidateAllDnsAsync(int applicationId, CancellationToken cancellationToken);
    Task<ApiActionResult<List<CertificateInfo>>> GetCertificatesAsync(int applicationId, CancellationToken cancellationToken);
    Task<ApiActionResult<RenewCertificateResponse>> RenewCertificateAsync(int applicationId, int certificateId, CancellationToken cancellationToken);
    Task<ApiActionResult> DeleteCertificateAsync(int applicationId, int certificateId, CancellationToken cancellationToken);
}
