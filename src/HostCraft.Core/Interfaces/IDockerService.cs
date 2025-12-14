using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using Docker.DotNet.Models;

namespace HostCraft.Core.Interfaces;

/// <summary>
/// Service for interacting with Docker daemon (containers, services, networks).
/// Handles both standalone Docker and Swarm mode operations.
/// </summary>
public interface IDockerService
{
    // Container operations (Standalone mode)
    Task<string> CreateContainerAsync(Server server, CreateContainerRequest request, CancellationToken cancellationToken = default);
    Task<string> CreateContainerAsync(Server server, CreateContainerParameters parameters, CancellationToken cancellationToken = default);
    Task<bool> StartContainerAsync(Server server, string containerId, CancellationToken cancellationToken = default);
    Task<bool> StopContainerAsync(Server server, string containerId, CancellationToken cancellationToken = default);
    Task<bool> RemoveContainerAsync(Server server, string containerId, CancellationToken cancellationToken = default);
    Task<IEnumerable<ContainerInfo>> ListContainersAsync(Server server, bool showAll = true, CancellationToken cancellationToken = default);
    Task<ContainerInspectInfo?> InspectContainerAsync(Server server, string containerId, CancellationToken cancellationToken = default);
    Task<Stream> GetContainerLogsAsync(Server server, string containerId, CancellationToken cancellationToken = default);
    
    // Service operations (Swarm mode)
    Task<string> CreateServiceAsync(Server server, CreateServiceRequest request, CancellationToken cancellationToken = default);
    Task<bool> UpdateServiceAsync(Server server, string serviceId, UpdateServiceRequest request, CancellationToken cancellationToken = default);
    Task<bool> RemoveServiceAsync(Server server, string serviceId, CancellationToken cancellationToken = default);
    Task<IEnumerable<ServiceInfo>> ListServicesAsync(Server server, CancellationToken cancellationToken = default);
    Task<ServiceInspectInfo?> InspectServiceAsync(Server server, string serviceId, CancellationToken cancellationToken = default);
    Task<Stream> GetServiceLogsAsync(Server server, string serviceId, CancellationToken cancellationToken = default);
    
    // Network operations
    Task<string> CreateNetworkAsync(Server server, CreateNetworkRequest request, CancellationToken cancellationToken = default);
    Task<bool> RemoveNetworkAsync(Server server, string networkId, CancellationToken cancellationToken = default);
    Task<IEnumerable<NetworkInfo>> ListNetworksAsync(Server server, CancellationToken cancellationToken = default);
    Task<NetworkInfo?> GetNetworkByNameAsync(Server server, string networkName, CancellationToken cancellationToken = default);
    Task<string> EnsureNetworkExistsAsync(Server server, string networkName, CancellationToken cancellationToken = default);
    
    // Image operations
    Task<bool> PullImageAsync(Server server, string imageName, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
    Task<string> BuildImageAsync(Server server, BuildImageRequest request, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
    Task<IEnumerable<ImageInfo>> ListImagesAsync(Server server, CancellationToken cancellationToken = default);
    
    // Swarm operations
    Task<bool> InitializeSwarmAsync(Server server, string advertiseAddress, CancellationToken cancellationToken = default);
    Task<string> GetSwarmJoinTokenAsync(Server server, bool isWorker = true, CancellationToken cancellationToken = default);
    Task<bool> JoinSwarmAsync(Server server, string managerAddress, string joinToken, CancellationToken cancellationToken = default);
    Task<bool> LeaveSwarmAsync(Server server, bool force = false, CancellationToken cancellationToken = default);
    Task<SwarmInfo?> InspectSwarmAsync(Server server, CancellationToken cancellationToken = default);
    
    // Server validation
    Task<bool> ValidateConnectionAsync(Server server, CancellationToken cancellationToken = default);
    Task<SystemInfo> GetSystemInfoAsync(Server server, CancellationToken cancellationToken = default);
}

// Request/Response models
public record CreateContainerRequest(
    string Name,
    string Image,
    Dictionary<string, string>? EnvironmentVariables = null,
    Dictionary<string, string>? Labels = null,
    List<string>? Networks = null,
    Dictionary<int, int>? PortBindings = null,
    long? MemoryLimit = null,
    long? CpuLimit = null);

public record CreateServiceRequest(
    string Name,
    string Image,
    int Replicas = 1,
    Dictionary<string, string>? EnvironmentVariables = null,
    Dictionary<string, string>? Labels = null,
    List<string>? Networks = null,
    int? Port = null,
    long? MemoryLimit = null,
    long? CpuLimit = null);

public record UpdateServiceRequest(
    string? Image = null,
    int? Replicas = null,
    Dictionary<string, string>? EnvironmentVariables = null,
    Dictionary<string, string>? Labels = null);

public record CreateNetworkRequest(
    string Name,
    NetworkType NetworkType,
    bool Attachable = true,
    Dictionary<string, string>? Labels = null);

public record BuildImageRequest(
    string Dockerfile,
    string Context,
    string Tag,
    Dictionary<string, string>? BuildArgs = null);

public record ContainerInfo(string Id, string Name, string Image, string State, DateTime Created);
public record ServiceInfo(string Id, string Name, string Image, int Replicas, DateTime Created);
public record NetworkInfo(string Id, string Name, string Driver, bool Attachable);
public record ContainerInspectInfo(string Id, string Name, string State, Dictionary<string, string> Labels);
public record ServiceInspectInfo(string Id, string Name, int Replicas, Dictionary<string, string> Labels);
public record SwarmInfo(string Id, bool IsManager, bool IsWorker, int Managers, int Workers);
public record SystemInfo(string OperatingSystem, string Architecture, bool SwarmActive, string DockerVersion);
public record ImageInfo(string Id, string Tag, long Size, DateTime Created);
