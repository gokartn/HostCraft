namespace HostCraft.Core.Interfaces;

/// <summary>
/// Enqueues deployment work for background processing.
/// </summary>
public interface IDeploymentJobQueue
{
    /// <summary>
    /// Adds a deployment job to the background queue.
    /// </summary>
    ValueTask EnqueueAsync(HostCraft.Core.Models.DeploymentJob job, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dequeues the next deployment to process. Blocks until available or cancelled.
    /// </summary>
    ValueTask<HostCraft.Core.Models.DeploymentJob> DequeueAsync(CancellationToken cancellationToken);
}
