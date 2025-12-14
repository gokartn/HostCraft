using Docker.DotNet;
using Docker.DotNet.Models;
using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using HostCraft.Core.Interfaces;
using System.Text;

namespace HostCraft.Infrastructure.Docker;

/// <summary>
/// Implementation of Docker operations using Docker.DotNet.
/// Handles both standalone containers and Swarm services.
/// </summary>
public class DockerService : IDockerService
{
    private readonly Dictionary<string, DockerClient> _clients = new();
    
    private DockerClient GetClient(Server server)
    {
        var key = $"{server.Host}:{server.Port}";
        
        if (!_clients.ContainsKey(key))
        {
            // For remote servers, use SSH tunnel or TCP
            // For local server, use Unix socket or named pipe
            var uri = server.Host == "localhost" || server.Host == "127.0.0.1"
                ? (Environment.OSVersion.Platform == PlatformID.Win32NT
                    ? "npipe://./pipe/docker_engine"
                    : "unix:///var/run/docker.sock")
                : $"tcp://{server.Host}:2375";
            
            _clients[key] = new DockerClientConfiguration(new Uri(uri)).CreateClient();
        }
        
        return _clients[key];
    }
    
    // Container operations
    public async Task<string> CreateContainerAsync(Server server, CreateContainerRequest request, CancellationToken cancellationToken = default)
    {
        var client = GetClient(server);
        
        var createParams = new CreateContainerParameters
        {
            Name = request.Name,
            Image = request.Image,
            Env = request.EnvironmentVariables?.Select(kv => $"{kv.Key}={kv.Value}").ToList(),
            Labels = request.Labels ?? new Dictionary<string, string>(),
            HostConfig = new HostConfig
            {
                NetworkMode = request.Networks?.FirstOrDefault(),
                PortBindings = request.PortBindings?.ToDictionary(
                    kv => $"{kv.Key}/tcp",
                    kv => (IList<PortBinding>)new List<PortBinding> { new() { HostPort = kv.Value.ToString() } }),
                Memory = request.MemoryLimit ?? 0,
                NanoCPUs = request.CpuLimit ?? 0
            }
        };
        
        var response = await client.Containers.CreateContainerAsync(createParams, cancellationToken);
        return response.ID;
    }

    public async Task<string> CreateContainerAsync(Server server, CreateContainerParameters parameters, CancellationToken cancellationToken = default)
    {
        var client = GetClient(server);
        var response = await client.Containers.CreateContainerAsync(parameters, cancellationToken);
        return response.ID;
    }
    
    public async Task<bool> StartContainerAsync(Server server, string containerId, CancellationToken cancellationToken = default)
    {
        var client = GetClient(server);
        return await client.Containers.StartContainerAsync(containerId, new ContainerStartParameters(), cancellationToken);
    }
    
    public async Task<bool> StopContainerAsync(Server server, string containerId, CancellationToken cancellationToken = default)
    {
        var client = GetClient(server);
        return await client.Containers.StopContainerAsync(containerId, new ContainerStopParameters(), cancellationToken);
    }
    
    public async Task<bool> RemoveContainerAsync(Server server, string containerId, CancellationToken cancellationToken = default)
    {
        var client = GetClient(server);
        await client.Containers.RemoveContainerAsync(containerId, new ContainerRemoveParameters { Force = true }, cancellationToken);
        return true;
    }
    
    public async Task<IEnumerable<ContainerInfo>> ListContainersAsync(Server server, bool showAll = true, CancellationToken cancellationToken = default)
    {
        var client = GetClient(server);
        var containers = await client.Containers.ListContainersAsync(new ContainersListParameters { All = showAll }, cancellationToken);
        
        return containers.Select(c => new ContainerInfo(
            c.ID,
            c.Names.FirstOrDefault()?.TrimStart('/') ?? "unknown",
            c.Image,
            c.State,
            c.Created));
    }
    
    public async Task<ContainerInspectInfo?> InspectContainerAsync(Server server, string containerId, CancellationToken cancellationToken = default)
    {
        var client = GetClient(server);
        var container = await client.Containers.InspectContainerAsync(containerId, cancellationToken);
        
        return new ContainerInspectInfo(
            container.ID,
            container.Name,
            container.State.Status,
            (Dictionary<string, string>)(container.Config.Labels ?? new Dictionary<string, string>()));
    }
    
    public async Task<Stream> GetContainerLogsAsync(Server server, string containerId, CancellationToken cancellationToken = default)
    {
        // TODO: Properly handle MultiplexedStream from Docker.DotNet
        // For now, return empty stream - this needs proper implementation
        await Task.CompletedTask;
        return Stream.Null;
    }
    
    // Service operations (Swarm)
    public async Task<string> CreateServiceAsync(Server server, CreateServiceRequest request, CancellationToken cancellationToken = default)
    {
        var client = GetClient(server);
        
        var serviceSpec = new ServiceCreateParameters
        {
            Service = new ServiceSpec
            {
                Name = request.Name,
                TaskTemplate = new TaskSpec
                {
                    ContainerSpec = new ContainerSpec
                    {
                        Image = request.Image,
                        Env = request.EnvironmentVariables?.Select(kv => $"{kv.Key}={kv.Value}").ToList(),
                        Labels = request.Labels ?? new Dictionary<string, string>()
                    },
                    // TODO: Add resource limits
                    Resources = null,
                    RestartPolicy = new SwarmRestartPolicy
                    {
                        Condition = "on-failure",
                        MaxAttempts = 3
                    }
                },
                Mode = new ServiceMode
                {
                    Replicated = new ReplicatedService { Replicas = (ulong)request.Replicas }
                },
                Networks = request.Networks?.Select(n => new NetworkAttachmentConfig { Target = n }).ToList(),
                UpdateConfig = new SwarmUpdateConfig
                {
                    Parallelism = 1,
                    Delay = 10_000_000_000, // 10 seconds in nanoseconds
                    FailureAction = "rollback"
                },
                Labels = request.Labels ?? new Dictionary<string, string>()
            }
        };
        
        // Add Traefik labels if port is specified
        if (request.Port.HasValue)
        {
            serviceSpec.Service.Labels[$"traefik.http.services.{request.Name}.loadbalancer.server.port"] = request.Port.Value.ToString();
        }
        
        var response = await client.Swarm.CreateServiceAsync(serviceSpec, cancellationToken);
        return response.ID;
    }
    
    public async Task<bool> UpdateServiceAsync(Server server, string serviceId, UpdateServiceRequest request, CancellationToken cancellationToken = default)
    {
        var client = GetClient(server);
        var service = await client.Swarm.InspectServiceAsync(serviceId, cancellationToken);
        
        var spec = service.Spec;
        
        if (request.Image != null)
        {
            spec.TaskTemplate.ContainerSpec.Image = request.Image;
        }
        
        if (request.Replicas.HasValue && spec.Mode?.Replicated != null)
        {
            spec.Mode.Replicated.Replicas = Convert.ToUInt64(request.Replicas.Value);
        }
        
        if (request.EnvironmentVariables != null)
        {
            spec.TaskTemplate.ContainerSpec.Env = request.EnvironmentVariables.Select(kv => $"{kv.Key}={kv.Value}").ToList();
        }
        
        await client.Swarm.UpdateServiceAsync(serviceId, new ServiceUpdateParameters { Service = spec, Version = Convert.ToInt64(service.Version.Index) }, cancellationToken);
        return true;
    }
    
    public async Task<bool> RemoveServiceAsync(Server server, string serviceId, CancellationToken cancellationToken = default)
    {
        var client = GetClient(server);
        await client.Swarm.RemoveServiceAsync(serviceId, cancellationToken);
        return true;
    }
    
    public async Task<IEnumerable<Core.Interfaces.ServiceInfo>> ListServicesAsync(Server server, CancellationToken cancellationToken = default)
    {
        var client = GetClient(server);
        var services = await client.Swarm.ListServicesAsync(cancellationToken: cancellationToken);
        
        return services.Select(s => new Core.Interfaces.ServiceInfo(
            s.ID,
            s.Spec.Name,
            s.Spec.TaskTemplate.ContainerSpec.Image,
            (int)(s.Spec.Mode.Replicated?.Replicas ?? 0),
            s.CreatedAt));
    }
    
    public async Task<ServiceInspectInfo?> InspectServiceAsync(Server server, string serviceId, CancellationToken cancellationToken = default)
    {
        var client = GetClient(server);
        var service = await client.Swarm.InspectServiceAsync(serviceId, cancellationToken);
        
        return new ServiceInspectInfo(
            service.ID,
            service.Spec.Name,
            (int)(service.Spec.Mode.Replicated?.Replicas ?? 0),
            (Dictionary<string, string>)(service.Spec.Labels ?? new Dictionary<string, string>()));
    }
    
    public async Task<Stream> GetServiceLogsAsync(Server server, string serviceId, CancellationToken cancellationToken = default)
    {
        var client = GetClient(server);
        return await client.Swarm.GetServiceLogsAsync(
            serviceId,
            new ServiceLogsParameters { ShowStdout = true, ShowStderr = true, Follow = true },
            cancellationToken);
    }
    
    // Network operations
    public async Task<string> CreateNetworkAsync(Server server, CreateNetworkRequest request, CancellationToken cancellationToken = default)
    {
        var client = GetClient(server);
        
        var createParams = new NetworksCreateParameters
        {
            Name = request.Name,
            Driver = request.NetworkType.ToString().ToLowerInvariant(),
            Attachable = request.Attachable,
            Labels = request.Labels ?? new Dictionary<string, string>()
        };
        
        var response = await client.Networks.CreateNetworkAsync(createParams, cancellationToken);
        return response.ID;
    }
    
    public async Task<bool> RemoveNetworkAsync(Server server, string networkId, CancellationToken cancellationToken = default)
    {
        var client = GetClient(server);
        await client.Networks.DeleteNetworkAsync(networkId, cancellationToken);
        return true;
    }
    
    public async Task<IEnumerable<NetworkInfo>> ListNetworksAsync(Server server, CancellationToken cancellationToken = default)
    {
        var client = GetClient(server);
        var networks = await client.Networks.ListNetworksAsync(cancellationToken: cancellationToken);
        
        return networks.Select(n => new NetworkInfo(
            n.ID,
            n.Name,
            n.Driver,
            n.Attachable));
    }
    
    public async Task<NetworkInfo?> GetNetworkByNameAsync(Server server, string networkName, CancellationToken cancellationToken = default)
    {
        var networks = await ListNetworksAsync(server, cancellationToken);
        return networks.FirstOrDefault(n => n.Name == networkName);
    }
    
    public async Task<string> EnsureNetworkExistsAsync(Server server, string networkName, CancellationToken cancellationToken = default)
    {
        var existing = await GetNetworkByNameAsync(server, networkName, cancellationToken);
        if (existing != null)
        {
            return existing.Id;
        }
        
        var networkType = server.IsSwarm ? NetworkType.Overlay : NetworkType.Bridge;
        return await CreateNetworkAsync(server, new CreateNetworkRequest(networkName, networkType), cancellationToken);
    }
    
    // Image operations
    public async Task<bool> PullImageAsync(Server server, string imageName, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var client = GetClient(server);
        
        await client.Images.CreateImageAsync(
            new ImagesCreateParameters { FromImage = imageName },
            null,
            new Progress<JSONMessage>(msg => progress?.Report(msg.Status ?? "")),
            cancellationToken);
        
        return true;
    }
    
    public async Task<string> BuildImageAsync(Server server, BuildImageRequest request, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var client = GetClient(server);
        
        // This would require tar archive creation for build context
        // Simplified version - in production, need to handle build context properly
        throw new NotImplementedException("Image building requires build context handling");
    }
    
    public async Task<IEnumerable<ImageInfo>> ListImagesAsync(Server server, CancellationToken cancellationToken = default)
    {
        var client = GetClient(server);
        var images = await client.Images.ListImagesAsync(new ImagesListParameters(), cancellationToken);
        
        return images.Select(i => new ImageInfo(
            i.ID,
            i.RepoTags?.FirstOrDefault() ?? "none",
            i.Size,
            i.Created));
    }
    
    // Swarm operations
    public async Task<bool> InitializeSwarmAsync(Server server, string advertiseAddress, CancellationToken cancellationToken = default)
    {
        var client = GetClient(server);
        
        await client.Swarm.InitSwarmAsync(new SwarmInitParameters
        {
            AdvertiseAddr = advertiseAddress,
            ListenAddr = "0.0.0.0:2377"
        }, cancellationToken);
        
        return true;
    }
    
    public async Task<string> GetSwarmJoinTokenAsync(Server server, bool isWorker = true, CancellationToken cancellationToken = default)
    {
        var client = GetClient(server);
        var swarm = await client.Swarm.InspectSwarmAsync(cancellationToken);
        
        return isWorker ? swarm.JoinTokens.Worker : swarm.JoinTokens.Manager;
    }
    
    public async Task<bool> JoinSwarmAsync(Server server, string managerAddress, string joinToken, CancellationToken cancellationToken = default)
    {
        var client = GetClient(server);
        
        await client.Swarm.JoinSwarmAsync(new SwarmJoinParameters
        {
            RemoteAddrs = new[] { managerAddress },
            JoinToken = joinToken
        }, cancellationToken);
        
        return true;
    }
    
    public async Task<bool> LeaveSwarmAsync(Server server, bool force = false, CancellationToken cancellationToken = default)
    {
        var client = GetClient(server);
        await client.Swarm.LeaveSwarmAsync(new SwarmLeaveParameters { Force = force }, cancellationToken);
        return true;
    }
    
    public async Task<SwarmInfo?> InspectSwarmAsync(Server server, CancellationToken cancellationToken = default)
    {
        var client = GetClient(server);
        var info = await GetSystemInfoAsync(server, cancellationToken);
        
        if (!info.SwarmActive)
        {
            return null;
        }
        
        var swarm = await client.Swarm.InspectSwarmAsync(cancellationToken);
        var nodes = await client.Swarm.ListNodesAsync(cancellationToken: cancellationToken);
        
        var managers = nodes.Count(n => n.Spec?.Role == "manager");
        var workers = nodes.Count(n => n.Spec?.Role == "worker");
        
        return new SwarmInfo(
            swarm.ID,
            nodes.Any(n => n.ManagerStatus?.Leader == true),
            nodes.Any(n => n.Spec?.Role == "worker"),
            managers,
            workers);
    }
    
    // Server validation
    public async Task<bool> ValidateConnectionAsync(Server server, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = GetClient(server);
            await client.System.PingAsync(cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }
    
    public async Task<SystemInfo> GetSystemInfoAsync(Server server, CancellationToken cancellationToken = default)
    {
        var client = GetClient(server);
        var info = await client.System.GetSystemInfoAsync(cancellationToken);
        
        return new SystemInfo(
            info.OperatingSystem,
            info.Architecture,
            info.Swarm?.LocalNodeState == "active",
            info.ServerVersion);
    }
}
