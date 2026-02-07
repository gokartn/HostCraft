using HostCraft.Api.Models.SystemSettings;

namespace HostCraft.Api.Services;

public interface ISystemSettingsWorkflowService
{
    Task<ApiActionResult<SystemSettingsDto>> GetSettingsAsync(CancellationToken cancellationToken);
    Task<ApiActionResult<ConfigureHostCraftResponse>> ConfigureHostCraftAsync(ConfigureHostCraftRequest request, CancellationToken cancellationToken);
    Task<ApiActionResult<ConfigureTraefikDashboardResponse>> ConfigureTraefikDashboardAsync(ConfigureTraefikDashboardRequest request, CancellationToken cancellationToken);
    Task<ApiActionResult<ContainerLogsResponse>> GetContainerLogsAsync(int lines, CancellationToken cancellationToken);
}
