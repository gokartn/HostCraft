using HostCraft.Core.Entities;

namespace HostCraft.Core.Interfaces.Repositories;

/// <summary>
/// Repository interface for Server entity operations
/// </summary>
public interface IServerRepository
{
    Task<Server?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<Server?> GetByIdWithPrivateKeyAsync(int id, CancellationToken cancellationToken = default);
    Task<Server?> GetByIdWithPrivateKeyAndRegionAsync(int id, CancellationToken cancellationToken = default);
    Task<Server?> GetByIdWithApplicationsAsync(int id, CancellationToken cancellationToken = default);
    Task<IEnumerable<Server>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<Server>> GetAllWithRegionAsync(CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<Server> Items, int TotalCount)> GetPagedWithRegionAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task<IEnumerable<Server>> GetSwarmManagersAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<Server>> GetSwarmManagersWithRegionAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<Server>> GetSwarmWorkersAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<Server>> GetNonWorkersWithRegionAsync(CancellationToken cancellationToken = default);
    Task<Server?> GetFirstReadyManagerAsync(CancellationToken cancellationToken = default);
    Task<int> CountReadyManagersAsync(CancellationToken cancellationToken = default);
    Task<Server> AddAsync(Server server, CancellationToken cancellationToken = default);
    Task UpdateAsync(Server server, CancellationToken cancellationToken = default);
    Task DeleteAsync(Server server, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default);
}
