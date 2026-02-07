using HostCraft.Core.Entities;

namespace HostCraft.Core.Interfaces.Repositories;

public interface ISystemSettingsRepository
{
    Task<SystemSettings?> GetAsync(CancellationToken cancellationToken = default);
    Task<SystemSettings> GetOrCreateAsync(CancellationToken cancellationToken = default);
    Task UpdateAsync(SystemSettings settings, CancellationToken cancellationToken = default);
}