using HostCraft.Core.Entities;
using HostCraft.Core.Enums;

namespace HostCraft.Core.Interfaces;

/// <summary>
/// Network management service with proper handling of bridge vs overlay networks.
/// CRITICAL: Ensures Swarm services use overlay networks and standalone containers use bridge networks.
/// </summary>
public interface INetworkManager
{
    /// <summary>
    /// Ensures the correct network type exists for the server mode.
    /// Validates existing networks and creates them if missing.
    /// </summary>
    Task<string> EnsureNetworkExistsAsync(Server server, string networkName, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Determines the correct network type based on server mode.
    /// Swarm servers use overlay, standalone uses bridge.
    /// </summary>
    NetworkType GetRequiredNetworkType(Server server);
    
    /// <summary>
    /// Validates that an existing network has the correct type for the server mode.
    /// </summary>
    Task<bool> ValidateNetworkTypeAsync(Server server, string networkName, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets the standard application network name.
    /// </summary>
    string GetApplicationNetworkName();
    
    /// <summary>
    /// Gets the platform network name (for HostCraft's own services).
    /// </summary>
    string GetPlatformNetworkName();
}
