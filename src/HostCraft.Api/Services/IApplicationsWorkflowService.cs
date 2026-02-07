using HostCraft.Api.Models.Applications;
using HostCraft.Api.Models.Shared;
using HostCraft.Core.Models;

namespace HostCraft.Api.Services;

public interface IApplicationsWorkflowService
{
    Task<ApiActionResult<IEnumerable<ApplicationDto>>> GetApplicationsAsync(int? serverId, int? projectId, CancellationToken cancellationToken);
    Task<ApiActionResult<PagedResult<ApplicationDto>>> GetApplicationsPagedAsync(int? serverId, int? projectId, int page, int pageSize, CancellationToken cancellationToken);
    Task<ApiActionResult<ApplicationWithDeploymentsDto>> GetApplicationAsync(int id, CancellationToken cancellationToken);
    Task<ApiActionResult<IEnumerable<ServerResponseDto>>> GetServersAsync(CancellationToken cancellationToken);
    Task<ApiActionResult<IEnumerable<ProjectDto>>> GetProjectsAsync(CancellationToken cancellationToken);
    Task<ApiActionResult<ApplicationDto>> CreateApplicationAsync(CreateApplicationRequest request, CancellationToken cancellationToken);
    Task<ApiActionResult> ScaleApplicationAsync(int id, int replicas, CancellationToken cancellationToken);
    Task<ApiActionResult> RedeployAsync(int id, CancellationToken cancellationToken);
    Task<ApiActionResult<string>> GetApplicationLogsAsync(int id, CancellationToken cancellationToken);
    Task<ApiActionResult<ApplicationWithDeploymentsDto>> UpdateApplicationAsync(int id, UpdateApplicationRequest request, CancellationToken cancellationToken);
    Task<ApiActionResult<TraefikPreviewResponse>> GetTraefikPreviewAsync(int id, CancellationToken cancellationToken);
    Task<ApiActionResult<TraefikPreviewResponse>> PreviewTraefikOverridesAsync(int id, TraefikOverridesRequest request, CancellationToken cancellationToken);
    Task<ApiActionResult> UpdateTraefikOverridesAsync(int id, TraefikOverridesRequest request, CancellationToken cancellationToken);
    Task<ApiActionResult> DeleteApplicationAsync(int id, CancellationToken cancellationToken);
    Task<ApiActionResult<ApplicationDto>> DeployComposeAsync(Core.Models.DeployComposeRequest request, CancellationToken cancellationToken);
    Task<ApiActionResult<ValidateComposeResponse>> ValidateComposeAsync(Core.Models.ValidateComposeRequest request, CancellationToken cancellationToken);
    Task<ApiActionResult<IEnumerable<StackInfoDto>>> ListStacksAsync(int? serverId, CancellationToken cancellationToken);
    Task<ApiActionResult> RemoveStackAsync(int id, CancellationToken cancellationToken);
    Task<ApiActionResult<ApplicationStatusDto>> GetApplicationStatusAsync(int id, CancellationToken cancellationToken);
    Task<ApiActionResult<ApplicationMetricsDto>> GetApplicationMetricsAsync(int id, CancellationToken cancellationToken);
    Task<ApiActionResult<OrphanedResourcesDto>> GetOrphanedResourcesAsync(int? serverId, CancellationToken cancellationToken);
    Task<ApiActionResult> CleanupOrphanedContainerAsync(string containerId, int serverId, CancellationToken cancellationToken);
    Task<ApiActionResult> CleanupOrphanedServiceAsync(string serviceId, int serverId, CancellationToken cancellationToken);
}
