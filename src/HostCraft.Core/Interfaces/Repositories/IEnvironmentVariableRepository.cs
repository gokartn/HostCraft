using HostCraft.Core.Entities;

namespace HostCraft.Core.Interfaces.Repositories;

/// <summary>
/// Repository interface for EnvironmentVariable entity operations
/// </summary>
public interface IEnvironmentVariableRepository
{
    Task<EnvironmentVariable?> GetByApplicationAndKeyAsync(int applicationId, string key, CancellationToken cancellationToken = default);
    Task<IEnumerable<EnvironmentVariable>> GetByApplicationAsync(int applicationId, CancellationToken cancellationToken = default);
    Task<IEnumerable<EnvironmentVariable>> GetSecretsAsync(CancellationToken cancellationToken = default);
    Task AddAsync(EnvironmentVariable environmentVariable, CancellationToken cancellationToken = default);
    Task UpdateAsync(EnvironmentVariable environmentVariable, CancellationToken cancellationToken = default);
    Task DeleteAsync(EnvironmentVariable environmentVariable, CancellationToken cancellationToken = default);
}
