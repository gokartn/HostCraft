using HostCraft.Core.Entities;

namespace HostCraft.Core.Interfaces;

/// <summary>
/// Service for deploying and managing Docker Stacks (docker stack deploy).
/// Stacks are collections of services defined in docker-compose.yml files.
/// </summary>
public interface IStackService
{
    /// <summary>
    /// Deploy a stack from a docker-compose.yml file.
    /// </summary>
    Task<StackDeploymentResult> DeployStackAsync(
        Server server, 
        string stackName, 
        string composeYaml, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Remove a deployed stack.
    /// </summary>
    Task<bool> RemoveStackAsync(
        Server server, 
        string stackName, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// List all deployed stacks on the server.
    /// </summary>
    Task<IEnumerable<StackInfo>> ListStacksAsync(
        Server server, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get detailed information about a specific stack.
    /// </summary>
    Task<StackDetails?> InspectStackAsync(
        Server server, 
        string stackName, 
        CancellationToken cancellationToken = default);
}

public record StackDeploymentResult(
    bool Success,
    string Message,
    int ServicesCreated,
    string? Error = null);

public record StackInfo(
    string Name,
    int ServiceCount,
    DateTime CreatedAt);

public record StackDetails(
    string Name,
    int ServiceCount,
    List<string> Services,
    List<string> Networks);
