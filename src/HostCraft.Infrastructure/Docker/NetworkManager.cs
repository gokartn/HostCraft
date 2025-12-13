using Docker.DotNet;
using Docker.DotNet.Models;
using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using HostCraft.Core.Interfaces;

namespace HostCraft.Infrastructure.Docker;

/// <summary>
/// Network management with proper handling of bridge vs overlay networks.
/// This is where we fix Coolify's critical mistakes.
/// </summary>
public class NetworkManager : INetworkManager
{
    private const string ApplicationNetworkName = "hostcraft-apps";
    private const string PlatformNetworkName = "hostcraft-platform";
    private const string ManagedLabel = "hostcraft.managed";
    
    private readonly IDockerService _dockerService;
    
    public NetworkManager(IDockerService dockerService)
    {
        _dockerService = dockerService;
    }
    
    public async Task<string> EnsureNetworkExistsAsync(Server server, string networkName, CancellationToken cancellationToken = default)
    {
        // Determine correct network type based on server mode
        var requiredNetworkType = GetRequiredNetworkType(server);
        
        // Check if network already exists
        var existingNetwork = await _dockerService.GetNetworkByNameAsync(server, networkName, cancellationToken);
        
        if (existingNetwork != null)
        {
            // Validate existing network has correct type
            ValidateExistingNetwork(existingNetwork, requiredNetworkType, networkName);
            return existingNetwork.Id;
        }
        
        // Create network with correct type
        var request = new CreateNetworkRequest(
            Name: networkName,
            NetworkType: requiredNetworkType,
            Attachable: true,
            Labels: new Dictionary<string, string>
            {
                [ManagedLabel] = "true",
                ["hostcraft.server.id"] = server.Id.ToString(),
                ["hostcraft.network.type"] = requiredNetworkType.ToString().ToLowerInvariant()
            });
        
        return await _dockerService.CreateNetworkAsync(server, request, cancellationToken);
    }
    
    public NetworkType GetRequiredNetworkType(Server server)
    {
        // CRITICAL LOGIC: Swarm servers MUST use overlay networks
        // Standalone servers use bridge networks
        return server.IsSwarm ? NetworkType.Overlay : NetworkType.Bridge;
    }
    
    public async Task<bool> ValidateNetworkTypeAsync(Server server, string networkName, CancellationToken cancellationToken = default)
    {
        var network = await _dockerService.GetNetworkByNameAsync(server, networkName, cancellationToken);
        
        if (network == null)
        {
            return false;
        }
        
        var requiredNetworkType = GetRequiredNetworkType(server);
        var actualNetworkType = ParseNetworkDriver(network.Driver);
        
        return actualNetworkType == requiredNetworkType;
    }
    
    public string GetApplicationNetworkName() => ApplicationNetworkName;
    
    public string GetPlatformNetworkName() => PlatformNetworkName;
    
    private void ValidateExistingNetwork(NetworkInfo network, NetworkType requiredType, string networkName)
    {
        var actualType = ParseNetworkDriver(network.Driver);
        
        if (actualType != requiredType)
        {
            throw new InvalidOperationException(
                $"Network '{networkName}' exists with driver '{network.Driver}' but '{requiredType.ToString().ToLowerInvariant()}' is required. " +
                $"This is a critical error that prevents deployment. " +
                $"Please manually remove the network using: docker network rm {networkName}");
        }
        
        if (requiredType == NetworkType.Overlay && !network.Attachable)
        {
            throw new InvalidOperationException(
                $"Network '{networkName}' exists but is not attachable. " +
                $"Overlay networks must be attachable for container attachment. " +
                $"Please remove and recreate: docker network rm {networkName}");
        }
    }
    
    private NetworkType ParseNetworkDriver(string driver)
    {
        return driver.ToLowerInvariant() switch
        {
            "bridge" => NetworkType.Bridge,
            "overlay" => NetworkType.Overlay,
            "host" => NetworkType.Host,
            "none" => NetworkType.None,
            _ => NetworkType.Bridge
        };
    }
}
