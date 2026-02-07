using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using Docker.DotNet.Models;

namespace HostCraft.Core.Interfaces;

/// <summary>
/// Service for interacting with Docker daemon (containers, services, networks).
/// Handles both standalone Docker and Swarm mode operations.
/// </summary>
public interface IDockerService : IDisposable
{
    // Client access
    Docker.DotNet.DockerClient GetDockerClient(Server server);

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
    Task<bool> RollbackServiceAsync(Server server, string serviceId, CancellationToken cancellationToken = default);
    Task<bool> RemoveServiceAsync(Server server, string serviceId, CancellationToken cancellationToken = default);
    Task<IEnumerable<ServiceInfo>> ListServicesAsync(Server server, CancellationToken cancellationToken = default);
    Task<ServiceInspectInfo?> InspectServiceAsync(Server server, string serviceId, CancellationToken cancellationToken = default);
    Task<Stream> GetServiceLogsAsync(Server server, string serviceId, CancellationToken cancellationToken = default);
    Task<Stream> GetTaskLogsAsync(Server server, string taskId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ServiceTaskContainerRef>> ListServiceTaskContainersAsync(Server server, string serviceId, CancellationToken cancellationToken = default);
    
    // Network operations
    Task<string> CreateNetworkAsync(Server server, CreateNetworkRequest request, CancellationToken cancellationToken = default);
    Task<bool> RemoveNetworkAsync(Server server, string networkId, CancellationToken cancellationToken = default);
    Task<IEnumerable<NetworkInfo>> ListNetworksAsync(Server server, CancellationToken cancellationToken = default);
    Task<NetworkInfo?> GetNetworkByNameAsync(Server server, string networkName, CancellationToken cancellationToken = default);
    Task<string> EnsureNetworkExistsAsync(Server server, string networkName, CancellationToken cancellationToken = default);
    Task<bool> ConnectContainerToNetworkAsync(Server server, string containerId, string networkName, CancellationToken cancellationToken = default);
    
    // Image operations
    Task<bool> PullImageAsync(Server server, string imageName, IProgress<string>? progress = null, RegistryAuthConfig? registryAuth = null, CancellationToken cancellationToken = default);
    Task<string> BuildImageAsync(Server server, BuildImageRequest request, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
    Task<IEnumerable<ImageInfo>> ListImagesAsync(Server server, CancellationToken cancellationToken = default);
    Task TagImageAsync(Server server, string sourceImage, string targetImage, CancellationToken cancellationToken = default);
    Task PushImageAsync(Server server, string imageName, IProgress<string>? progress = null, RegistryAuthConfig? registryAuth = null, CancellationToken cancellationToken = default);
    Task<bool> ImageExistsAsync(Server server, string imageName, CancellationToken cancellationToken = default);

    // Volume operations
    Task<string> CreateVolumeAsync(Server server, VolumeCreateRequest request, CancellationToken cancellationToken = default);
    Task<bool> RemoveVolumeAsync(Server server, string volumeName, bool force = false, CancellationToken cancellationToken = default);
    Task<bool> VolumeExistsAsync(Server server, string volumeName, CancellationToken cancellationToken = default);
    Task<IEnumerable<VolumeInfo>> ListVolumesAsync(Server server, CancellationToken cancellationToken = default);

    // Swarm operations
    Task<bool> InitializeSwarmAsync(Server server, string advertiseAddress, CancellationToken cancellationToken = default);
    Task<string> GetSwarmJoinTokenAsync(Server server, bool isWorker = true, CancellationToken cancellationToken = default);
    Task<bool> JoinSwarmAsync(Server server, string managerAddress, string joinToken, CancellationToken cancellationToken = default);
    Task<bool> LeaveSwarmAsync(Server server, bool force = false, CancellationToken cancellationToken = default);
    Task<SwarmInfo?> InspectSwarmAsync(Server server, CancellationToken cancellationToken = default);
    Task<bool> IsSwarmActiveAsync(Server server, CancellationToken cancellationToken = default);
    Task<string?> GetSwarmManagerAddressAsync(Server server, CancellationToken cancellationToken = default);
    
    // Swarm node management
    Task<IEnumerable<NodeInfo>> ListNodesAsync(Server server, CancellationToken cancellationToken = default);
    Task<NodeInfo?> InspectNodeAsync(Server server, string nodeId, CancellationToken cancellationToken = default);
    Task<bool> UpdateNodeAsync(Server server, string nodeId, NodeUpdateRequest request, CancellationToken cancellationToken = default);
    Task<bool> RemoveNodeAsync(Server server, string nodeId, bool force = false, CancellationToken cancellationToken = default);
    Task<(string WorkerToken, string ManagerToken)> GetJoinTokensAsync(Server server, CancellationToken cancellationToken = default);

    // Runtime metrics
    Task<ContainerResourceUsage?> GetContainerStatsAsync(Server server, string containerId, CancellationToken cancellationToken = default);

    // Task-level visibility
    Task<IEnumerable<ServiceTaskInfo>> ListServiceTasksAsync(Server server, string serviceId, CancellationToken cancellationToken = default);
    
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
    Dictionary<string, string>? Volumes = null,
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
    List<ServicePortMapping>? PortMappings = null,
    Dictionary<string, string>? Mounts = null,
    long? MemoryLimit = null,
    long? CpuLimit = null,
    RegistryAuthConfig? RegistryAuth = null,
    ServiceUpdateConfig? UpdateConfig = null,
    ServiceRollbackConfig? RollbackConfig = null,
    ServiceHealthCheckConfig? HealthCheck = null,
    ServicePlacementConfig? PlacementConfig = null);

public record ServicePortMapping(
    int HostPort,
    int ContainerPort,
    string Protocol = "tcp");

public record UpdateServiceRequest(
    string? Image = null,
    int? Replicas = null,
    Dictionary<string, string>? EnvironmentVariables = null,
    Dictionary<string, string>? Labels = null,
    List<string>? Networks = null,
    RegistryAuthConfig? RegistryAuth = null,
    ServiceUpdateConfig? UpdateConfig = null,
    ServiceRollbackConfig? RollbackConfig = null,
    ServiceHealthCheckConfig? HealthCheck = null);

public record RegistryAuthConfig(
    string ServerAddress,
    string? Username = null,
    string? Password = null);

public record CreateNetworkRequest(
    string Name,
    NetworkType NetworkType,
    bool Attachable = true,
    Dictionary<string, string>? Labels = null);

public record BuildImageRequest(
    string Dockerfile,
    string Context,
    string Tag,
    Dictionary<string, string>? BuildArgs = null,
    string? Target = null);

public record ContainerPortInfo(int PrivatePort, int? PublicPort, string Type, string? Ip);

public record ContainerInfo(
    string Id,
    string Name,
    string Image,
    string State,
    DateTime Created,
    IReadOnlyList<ContainerPortInfo> PublishedPorts);
public record ServiceInfo(string Id, string Name, string Image, int Replicas, DateTime Created, IReadOnlyList<ServicePublishedPort> PublishedPorts);
public record NetworkInfo(string Id, string Name, string Driver, bool Attachable);
public record ContainerInspectInfo(string Id, string Name, string State, Dictionary<string, string> Labels);
public record ServiceInspectInfo(string Id, string Name, int Replicas, Dictionary<string, string> Labels);
public record SwarmInfo(string Id, bool IsManager, bool IsWorker, int Managers, int Workers);
public record SystemInfo(
    string OperatingSystem,
    string Architecture,
    bool SwarmActive,
    string DockerVersion,
    string? Hostname = null,
    string? SwarmNodeId = null,
    string? SwarmId = null,
    string? SwarmNodeAddress = null,
    string? SwarmNodeState = null,
    string? SwarmNodeAvailability = null,
    bool IsSwarmManager = false,
    bool IsSwarmLeader = false);
public record ImageInfo(string Id, string Tag, long Size, DateTime Created);
public record NodeInfo(
    string Id, 
    string Hostname, 
    string Role, 
    string State, 
    string Availability, 
    bool IsLeader,
    string Address,
    long NanoCPUs,
    long MemoryBytes,
    string EngineVersion,
    string Platform);
public record NodeUpdateRequest(string? Role = null, string? Availability = null);
public record ServicePublishedPort(int PublishedPort, int TargetPort, string Protocol, string PublishMode);
public record ServiceTaskInfo(
    string Id,
    string NodeId,
    string? DesiredState,
    string? CurrentState,
    string? Error,
    int Slot,
    DateTime? UpdatedAt);

public record ServiceTaskContainerRef(
    string TaskId,
    string? ContainerId,
    string NodeId,
    string? NodeName,
    string? DesiredState,
    string? CurrentState,
    int Slot,
    DateTime? UpdatedAt);

public record ContainerResourceUsage(
    double CpuPercent,
    long MemoryUsageBytes,
    long MemoryLimitBytes,
    double MemoryPercent,
    long NetworkRxBytes,
    long NetworkTxBytes,
    long BlockReadBytes,
    long BlockWriteBytes,
    DateTime Timestamp);

/// <summary>
/// Configuration for Docker Swarm service rolling updates.
/// Controls how updates are applied to service replicas.
/// </summary>
public record ServiceUpdateConfig(
    /// <summary>
    /// Update order: "start-first" (zero-downtime, start new before stopping old) or "stop-first" (default, stop old before starting new)
    /// For HA/DR, always use "start-first"
    /// </summary>
    string Order = "start-first",
    /// <summary>
    /// Number of replicas to update simultaneously (default: 1 for controlled rollout)
    /// </summary>
    ulong Parallelism = 1,
    /// <summary>
    /// Delay between updating replica batches in seconds (default: 10 seconds)
    /// </summary>
    int DelaySeconds = 10,
    /// <summary>
    /// Action to take on update failure: "pause" (default), "continue", or "rollback"
    /// For HA/DR, use "rollback" for automatic recovery
    /// </summary>
    string FailureAction = "rollback",
    /// <summary>
    /// Max failure rate (0.0-1.0) before triggering FailureAction (default: 0 = any failure triggers)
    /// </summary>
    float MaxFailureRatio = 0.0f);

/// <summary>
/// Configuration for automatic rollback on service update failure.
/// </summary>
public record ServiceRollbackConfig(
    /// <summary>
    /// Number of replicas to roll back simultaneously (default: 1)
    /// </summary>
    ulong Parallelism = 1,
    /// <summary>
    /// Delay between rollback batches in seconds (default: 5 seconds for faster recovery)
    /// </summary>
    int DelaySeconds = 5,
    /// <summary>
    /// Action to take on rollback failure: "pause" or "continue" (default: pause)
    /// </summary>
    string FailureAction = "pause",
    /// <summary>
    /// Max failure rate (0.0-1.0) before triggering FailureAction
    /// </summary>
    float MaxFailureRatio = 0.0f);

/// <summary>
/// Health check configuration for Docker containers and services.
/// Required for start-first updates to work correctly.
/// </summary>
public record ServiceHealthCheckConfig(
    /// <summary>
    /// Health check test command (e.g., ["CMD", "curl", "-f", "http://localhost/health"])
    /// </summary>
    List<string> Test,
    /// <summary>
    /// Time between health checks in seconds (default: 30 seconds)
    /// </summary>
    int IntervalSeconds = 30,
    /// <summary>
    /// Time to wait for health check to complete in seconds (default: 10 seconds)
    /// </summary>
    int TimeoutSeconds = 10,
    /// <summary>
    /// Number of consecutive failures before marking unhealthy (default: 3)
    /// </summary>
    int Retries = 3,
    /// <summary>
    /// Start period before retries count toward failures in seconds (default: 60 seconds)
    /// Gives the service time to initialize
    /// </summary>
    int StartPeriodSeconds = 60);

/// <summary>
/// Placement configuration for Docker Swarm services.
/// Controls how replicas are distributed across nodes.
/// </summary>
public record ServicePlacementConfig(
    /// <summary>
    /// Placement constraints (e.g., ["node.role==worker", "node.labels.region==us-east"])
    /// </summary>
    List<string>? Constraints = null,
    /// <summary>
    /// Placement preferences for spreading replicas (e.g., [{"Spread":"node.id"}])
    /// </summary>
    List<PlacementPreference>? Preferences = null,
    /// <summary>
    /// Maximum replicas per node (null = unlimited, 1 = max distribution)
    /// </summary>
    ulong? MaxReplicasPerNode = null);

/// <summary>
/// Placement preference for spreading replicas across nodes.
/// </summary>
public record PlacementPreference(
    /// <summary>
    /// Spread descriptor (e.g., "node.id" for even distribution, "node.labels.zone" for zones)
    /// </summary>
    string Spread);

/// <summary>
/// Request to create a Docker volume.
/// </summary>
public record VolumeCreateRequest(
    string Name,
    string Driver = "local",
    Dictionary<string, string>? DriverOpts = null,
    Dictionary<string, string>? Labels = null);

/// <summary>
/// Information about a Docker volume.
/// </summary>
public record VolumeInfo(
    string Name,
    string Driver,
    string MountPoint,
    Dictionary<string, string>? Labels = null);
