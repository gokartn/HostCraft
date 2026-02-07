using System.Collections.Generic;
using HostCraft.Core.Entities;

namespace HostCraft.Core.Interfaces.Repositories;

/// <summary>
/// Repository interface for Application entity operations
/// </summary>
public interface IApplicationRepository
{
    Task<Application?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<Application?> GetByIdWithServerAsync(int id, CancellationToken cancellationToken = default);
    Task<Application?> GetByIdWithServerAndDomainsAsync(int id, CancellationToken cancellationToken = default);
    Task<Application?> GetByIdWithAllRelationsAsync(int id, CancellationToken cancellationToken = default);
    Task<Application?> GetByIdWithServerAndEnvironmentAsync(int id, CancellationToken cancellationToken = default);
    Task<Application?> GetByIdWithServerAndDeploymentsAsync(int id, CancellationToken cancellationToken = default);
    Task<Application?> GetByIdWithServerProjectAndDeploymentsAsync(int id, CancellationToken cancellationToken = default);
    Task<Application?> GetByUuidWithGitProviderAndServerAsync(Guid uuid, CancellationToken cancellationToken = default);
    Task<IEnumerable<Application>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<Application>> GetByProjectIdAsync(int projectId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Application>> GetAllWithServerProjectAndLatestDeploymentAsync(int? serverId = null, int? projectId = null, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<Application> Items, int TotalCount)> GetPagedWithServerProjectAndLatestDeploymentAsync(int? serverId, int? projectId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<IEnumerable<Application>> GetByServerIdAsync(int serverId, CancellationToken cancellationToken = default);
    Task<int> CountByServerIdAsync(int serverId, CancellationToken cancellationToken = default);
    Task<Application?> GetByServerAndNameAsync(int serverId, string name, CancellationToken cancellationToken = default);
    Task<Application> AddAsync(Application application, CancellationToken cancellationToken = default);
    Task UpdateAsync(Application application, CancellationToken cancellationToken = default);
    Task DeleteAsync(Application application, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default);
    Task ReplaceNonSecretEnvironmentVariablesAsync(Application application, IDictionary<string, string> environmentVariables, CancellationToken cancellationToken = default);
}
