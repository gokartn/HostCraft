using HostCraft.Core.Entities;

namespace HostCraft.Core.Interfaces.Repositories;

/// <summary>
/// Repository interface for Domain entity operations
/// </summary>
public interface IDomainRepository
{
    Task<Domain?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<Domain?> GetByIdWithApplicationAsync(int id, CancellationToken cancellationToken = default);
    Task<IEnumerable<Domain>> GetByApplicationIdAsync(int applicationId, CancellationToken cancellationToken = default);
    Task<Domain?> GetByHostAndPathAsync(string host, string path, CancellationToken cancellationToken = default);
    Task<IEnumerable<Domain>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<Domain> Items, int TotalCount)> GetByApplicationIdPagedAsync(int applicationId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<Domain> AddAsync(Domain domain, CancellationToken cancellationToken = default);
    Task UpdateAsync(Domain domain, CancellationToken cancellationToken = default);
    Task DeleteAsync(Domain domain, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> ExistsByHostAndPathAsync(string host, string path, CancellationToken cancellationToken = default);
}
