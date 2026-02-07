using HostCraft.Core.Entities;
using HostCraft.Core.Enums;

namespace HostCraft.Core.Interfaces.Repositories;

public interface IGitProviderSettingsRepository
{
    Task<List<GitProviderSettings>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<GitProviderSettings?> GetByTypeAndApiUrlAsync(GitProviderType type, string? apiUrl, CancellationToken cancellationToken = default);
    Task<GitProviderSettings> AddAsync(GitProviderSettings settings, CancellationToken cancellationToken = default);
    Task UpdateAsync(GitProviderSettings settings, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
}