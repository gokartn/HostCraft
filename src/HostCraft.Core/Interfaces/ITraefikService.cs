namespace HostCraft.Core.Interfaces;

/// <summary>
/// Manages Traefik proxy routing configuration for Docker Swarm services.
/// </summary>
public interface ITraefikService
{
    /// <summary>
    /// Updates a Docker Swarm service with Traefik routing labels based on the application's domain configuration.
    /// </summary>
    /// <param name="applicationId">The application ID to update Traefik labels for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpdateServiceLabelsAsync(int applicationId, CancellationToken cancellationToken = default);
}
