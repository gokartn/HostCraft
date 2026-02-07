using System.Linq;
using HostCraft.Core.Interfaces;
using HostCraft.Core.Interfaces.Repositories;
using HostCraft.Core.Models;
using HostCraft.Core.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HostCraft.Infrastructure.BackgroundJobs;

/// <summary>
/// Background worker that drains deployment jobs and executes deployments sequentially.
/// </summary>
public class DeploymentWorker : BackgroundService
{
    private readonly IDeploymentJobQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DeploymentWorker> _logger;
    private const int MaxAttempts = 3;

    public DeploymentWorker(
        IDeploymentJobQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<DeploymentWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Deployment worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            DeploymentJob job;
            try
            {
                job = await _queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await ProcessJobAsync(job, stoppingToken);
        }

        _logger.LogInformation("Deployment worker stopping");
    }

    private async Task ProcessJobAsync(DeploymentJob job, CancellationToken stoppingToken)
    {
        if (job.Type == DeploymentJobType.Deploy)
        {
            await ProcessDeploymentAsync(job.DeploymentId, stoppingToken);
            return;
        }

        if (job.Type == DeploymentJobType.CleanupPreview)
        {
            await ProcessPreviewCleanupAsync(job, stoppingToken);
            return;
        }
    }

    private async Task ProcessDeploymentAsync(int deploymentId, CancellationToken stoppingToken)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            using var scope = _scopeFactory.CreateScope();
            var orchestrator = scope.ServiceProvider.GetRequiredService<IDeploymentOrchestrator>();
            var deploymentRepository = scope.ServiceProvider.GetRequiredService<IDeploymentRepository>();

            try
            {
                _logger.LogInformation("Starting deployment {DeploymentId} (attempt {Attempt}/{MaxAttempts})", deploymentId, attempt, MaxAttempts);
                await orchestrator.DeployAsync(deploymentId, stoppingToken);
                _logger.LogInformation("Deployment {DeploymentId} completed", deploymentId);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Deployment {DeploymentId} cancelled", deploymentId);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Deployment {DeploymentId} failed on attempt {Attempt}", deploymentId, attempt);

                if (attempt == MaxAttempts)
                {
                    var deployment = await deploymentRepository.GetByIdAsync(deploymentId, stoppingToken);
                    if (deployment != null)
                    {
                        deployment.ErrorMessage = ex.Message;
                        deployment.Status = HostCraft.Core.Enums.DeploymentStatus.Failed;
                        deployment.FinishedAt = DateTime.UtcNow;
                        await deploymentRepository.UpdateAsync(deployment, stoppingToken);
                    }
                }
                else
                {
                    await Task.Delay(TimeSpan.FromSeconds(5 * attempt), stoppingToken);
                }
            }
        }

        // End of deployment processing
    }

    private async Task ProcessPreviewCleanupAsync(DeploymentJob job, CancellationToken stoppingToken)
    {
        if (!job.ApplicationId.HasValue || string.IsNullOrWhiteSpace(job.PreviewId))
        {
            _logger.LogWarning("Preview cleanup job missing application or preview id");
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var applicationRepository = scope.ServiceProvider.GetRequiredService<IApplicationRepository>();
        var deploymentRepository = scope.ServiceProvider.GetRequiredService<IDeploymentRepository>();
        var dockerService = scope.ServiceProvider.GetRequiredService<IDockerService>();

        var application = await applicationRepository.GetByIdWithServerAsync(job.ApplicationId.Value, stoppingToken);
        if (application == null || application.Server == null)
        {
            _logger.LogWarning("Preview cleanup skipped: application {ApplicationId} not found or missing server", job.ApplicationId);
            return;
        }

        var previewDeployments = await deploymentRepository.GetPreviewDeploymentsAsync(application.Id, job.PreviewId, stoppingToken);
        if (!previewDeployments.Any())
        {
            _logger.LogInformation("No preview deployments found for {PreviewId}", job.PreviewId);
            return;
        }

        var previewServiceName = $"{application.Name.ToLower().Replace(' ', '-')}-{job.PreviewId}";

        try
        {
            if (application.Server.IsSwarm)
            {
                var services = await dockerService.ListServicesAsync(application.Server, stoppingToken);
                var previewService = services.FirstOrDefault(s => s.Name == previewServiceName);
                if (previewService != null)
                {
                    await dockerService.RemoveServiceAsync(application.Server, previewService.Id, stoppingToken);
                    _logger.LogInformation("Removed preview service {ServiceName}", previewServiceName);
                }
            }
            else
            {
                var containers = await dockerService.ListContainersAsync(application.Server, cancellationToken: stoppingToken);
                var previewContainer = containers.FirstOrDefault(c => c.Name == previewServiceName);
                if (previewContainer != null)
                {
                    await dockerService.StopContainerAsync(application.Server, previewContainer.Id, stoppingToken);
                    await dockerService.RemoveContainerAsync(application.Server, previewContainer.Id, stoppingToken);
                    _logger.LogInformation("Removed preview container {ContainerName}", previewServiceName);
                }
            }

            foreach (var deployment in previewDeployments)
            {
                if (deployment.Status == DeploymentStatus.Running || deployment.Status == DeploymentStatus.Queued)
                {
                    deployment.Status = DeploymentStatus.Cancelled;
                    deployment.FinishedAt = DateTime.UtcNow;
                }
            }

            await deploymentRepository.UpdateRangeAsync(previewDeployments, stoppingToken);
            _logger.LogInformation("Cleaned up preview deployments for {PreviewId}", job.PreviewId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning up preview deployment {PreviewId}", job.PreviewId);
        }
    }
}
