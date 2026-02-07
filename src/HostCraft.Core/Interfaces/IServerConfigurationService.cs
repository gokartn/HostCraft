using HostCraft.Core.Entities;

namespace HostCraft.Core.Interfaces;

/// <summary>
/// Service for automatically configuring servers with Docker and optional Swarm setup
/// </summary>
public interface IServerConfigurationService
{
    /// <summary>
    /// Automatically installs Docker on a server and configures it based on server type (Swarm Manager/Worker)
    /// </summary>
    Task AutoConfigureServerAsync(int serverId);

    /// <summary>
    /// Start auto-configuration in the background with basic safety checks.
    /// </summary>
    Task<ServerConfigurationResult> StartAutoConfigureAsync(int serverId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of starting auto-configuration.
/// </summary>
public record ServerConfigurationResult(
    bool Success,
    string Message,
    bool NotFound = false,
    string? ErrorDetails = null);
