using HostCraft.Core.Entities;

namespace HostCraft.Core.Interfaces.Repositories;

public interface IGitProviderRepository
{
    Task<List<GitProvider>> GetByUserAsync(int userId, CancellationToken cancellationToken = default);
    Task<GitProvider?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task AddAsync(GitProvider provider, CancellationToken cancellationToken = default);
    Task UpdateAsync(GitProvider provider, CancellationToken cancellationToken = default);
    Task DeleteAsync(GitProvider provider, CancellationToken cancellationToken = default);
}