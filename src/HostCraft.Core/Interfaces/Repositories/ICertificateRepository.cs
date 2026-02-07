using HostCraft.Core.Entities;

namespace HostCraft.Core.Interfaces.Repositories;

public interface ICertificateRepository
{
    Task<List<Certificate>> GetByApplicationIdAsync(int applicationId, CancellationToken cancellationToken = default);
    Task<Certificate?> GetByIdAndApplicationAsync(int certificateId, int applicationId, CancellationToken cancellationToken = default);
    Task DeleteAsync(Certificate certificate, CancellationToken cancellationToken = default);
}