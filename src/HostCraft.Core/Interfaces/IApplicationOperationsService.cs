using System.Collections.Generic;
using System.IO;
using HostCraft.Core.Models;
using HostCraft.Core.Models.Applications.Operations;
using HostCraft.Core.Models.Results;

namespace HostCraft.Core.Interfaces;

/// <summary>
/// Orchestrates runtime application operations (logs, redeploy, compose, orphan cleanup).
/// </summary>
public interface IApplicationOperationsService
{
    Task<OperationResult<DeploymentQueueResult>> RedeployAsync(int applicationId, CancellationToken cancellationToken = default);
    Task<OperationResult<Stream>> GetLogsAsync(int applicationId, CancellationToken cancellationToken = default);
    Task<OperationResult<ApplicationStatusInfo>> GetStatusAsync(int applicationId, CancellationToken cancellationToken = default);
    Task<OperationResult<OrphanedResourcesInfo>> GetOrphanedResourcesAsync(int? serverId, CancellationToken cancellationToken = default);
    Task<OperationResult<bool>> CleanupOrphanedContainerAsync(string containerId, int serverId, CancellationToken cancellationToken = default);
    Task<OperationResult<bool>> CleanupOrphanedServiceAsync(string serviceId, int serverId, CancellationToken cancellationToken = default);
    Task<OperationResult<ApplicationComposeResult>> DeployComposeAsync(DeployComposeRequest request, CancellationToken cancellationToken = default);
    Task<OperationResult<ComposeValidationDetails>> ValidateComposeAsync(string composeFile, CancellationToken cancellationToken = default);
    Task<OperationResult<IEnumerable<StackSummary>>> ListStacksAsync(int? serverId, CancellationToken cancellationToken = default);
    Task<OperationResult<bool>> RemoveStackAsync(int applicationId, CancellationToken cancellationToken = default);
    Task<OperationResult<ApplicationRuntimeMetrics>> GetMetricsAsync(int applicationId, CancellationToken cancellationToken = default);
    Task<OperationResult<IReadOnlyList<ServiceTaskContainerRef>>> GetServiceTasksAsync(int applicationId, CancellationToken cancellationToken = default);
    Task<OperationResult<Stream>> GetTaskLogsAsync(int applicationId, string taskId, CancellationToken cancellationToken = default);
}
