using System.Threading;
using System.Threading.Tasks;
using HostCraft.Core.Entities;

namespace HostCraft.Core.Interfaces.Repositories;

/// <summary>
/// Repository interface for Project entity operations
/// </summary>
public interface IProjectRepository
{
    Task<Project?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<Project?> GetByIdWithApplicationsAsync(int id, CancellationToken cancellationToken = default);
    Task<IEnumerable<Project>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<Project>> GetAllWithApplicationsAsync(CancellationToken cancellationToken = default);
    Task<Project?> GetByNameAsync(string name, CancellationToken cancellationToken = default);
    Task<bool> ExistsByNameAsync(string name, int? excludeId = null, CancellationToken cancellationToken = default);
    Task<Project> AddAsync(Project project, CancellationToken cancellationToken = default);
    Task UpdateAsync(Project project, CancellationToken cancellationToken = default);
    Task DeleteAsync(Project project, CancellationToken cancellationToken = default);
    Task<Project> GetOrCreateGlobalAsync(string description, CancellationToken cancellationToken = default);
}
