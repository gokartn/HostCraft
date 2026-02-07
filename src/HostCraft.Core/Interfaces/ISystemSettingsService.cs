using HostCraft.Core.Entities;
using HostCraft.Core.Models.Results;
using HostCraft.Core.Models.SystemSettings;

namespace HostCraft.Core.Interfaces;

public interface ISystemSettingsService
{
    Task<SystemSettings?> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task<OperationResult<SystemSettings>> ConfigureHostCraftAsync(ConfigureHostCraftCommand command, CancellationToken cancellationToken = default);
    Task<OperationResult<SystemSettings>> ConfigureTraefikDashboardAsync(ConfigureTraefikDashboardCommand command, CancellationToken cancellationToken = default);
    Task<OperationResult<ContainerLogsResult>> GetContainerLogsAsync(int lines, CancellationToken cancellationToken = default);
}
