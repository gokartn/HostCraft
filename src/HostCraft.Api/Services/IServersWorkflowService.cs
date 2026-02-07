using HostCraft.Api.Models.Servers;
using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using HostCraft.Core.Interfaces;

namespace HostCraft.Api.Services;

public interface IServersWorkflowService
{
    Task<ApiActionResult<IEnumerable<ServerListDto>>> GetServersAsync(bool paged, int page, int pageSize, CancellationToken cancellationToken);
    Task<ApiActionResult<Server>> GetServerAsync(int id, CancellationToken cancellationToken);
    Task<ApiActionResult<Server>> CreateServerAsync(CreateServerRequest request, CancellationToken cancellationToken);
    Task<ApiActionResult> UpdateServerAsync(int id, UpdateServerRequest request, CancellationToken cancellationToken);
    Task<ApiActionResult> DeleteServerAsync(int id, CancellationToken cancellationToken);
    Task<ApiActionResult<Server>> ConfigureLocalhostAsync(CancellationToken cancellationToken);
    Task<ApiActionResult<ServerValidationResult>> ValidateNewServerAsync(CreateServerRequest request, CancellationToken cancellationToken);
    Task<ApiActionResult<ServerValidationResult>> ValidateExistingServerAsync(int id, CancellationToken cancellationToken);
    Task<ApiActionResult<IEnumerable<ContainerInfo>>> GetContainersAsync(int id, CancellationToken cancellationToken);
    Task<ApiActionResult<IEnumerable<ServiceInfo>>> GetServicesAsync(int id, CancellationToken cancellationToken);
    Task<ApiActionResult> RefreshSwarmStatusAsync(int id, CancellationToken cancellationToken);
    Task<ApiActionResult> InitializeSwarmAsync(int id, CancellationToken cancellationToken);
    Task<ApiActionResult> JoinAsManagerAsync(int existingManagerId, JoinManagerRequest request, CancellationToken cancellationToken);
    Task<ApiActionResult> PromoteToManagerAsync(int id, CancellationToken cancellationToken);
    Task<ApiActionResult> AutoConfigureServerAsync(int id, CancellationToken cancellationToken);
    Task<ApiActionResult<SystemInfo>> GetSystemInfoAsync(int id, CancellationToken cancellationToken);
    Task<ApiActionResult> UpdateWizardStepAsync(int id, WizardStepUpdate request, CancellationToken cancellationToken);
    Task<ApiActionResult> CompleteWizardAsync(int id, CancellationToken cancellationToken);
    Task<ApiActionResult<object>> GetPublicKeyAsync(int id, CancellationToken cancellationToken);
    Task<ApiActionResult> GetJoinTokensAsync(int id, CancellationToken cancellationToken);
}
