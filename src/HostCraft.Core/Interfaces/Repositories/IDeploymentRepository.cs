using System.Threading;
using System.Threading.Tasks;
using HostCraft.Core.Entities;
using HostCraft.Core.Enums;

namespace HostCraft.Core.Interfaces.Repositories;

/// <summary>
/// Repository interface for Deployment entity operations
/// </summary>
public interface IDeploymentRepository
{
    Task<Deployment?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<Deployment?> GetByIdWithApplicationAsync(int id, CancellationToken cancellationToken = default);
    Task<Deployment?> GetByIdWithApplicationAndGitProviderAsync(int id, CancellationToken cancellationToken = default);
    Task<Deployment?> GetByIdWithApplicationAndLogsAsync(int id, CancellationToken cancellationToken = default);
    Task<Deployment?> GetByIdWithApplicationDetailsAsync(int id, CancellationToken cancellationToken = default);
    Task<IEnumerable<Deployment>> GetDeploymentsAsync(int? applicationId, DeploymentStatus? status, int limit, CancellationToken cancellationToken = default);
    Task<IEnumerable<Deployment>> GetPreviewDeploymentsAsync(int applicationId, string previewId, CancellationToken cancellationToken = default);
    Task<IEnumerable<DeploymentLog>> GetLogsAfterAsync(int deploymentId, int afterId, CancellationToken cancellationToken = default);
    Task<Deployment> AddAsync(Deployment deployment, CancellationToken cancellationToken = default);
    Task UpdateAsync(Deployment deployment, CancellationToken cancellationToken = default);
    Task UpdateRangeAsync(IEnumerable<Deployment> deployments, CancellationToken cancellationToken = default);
}
