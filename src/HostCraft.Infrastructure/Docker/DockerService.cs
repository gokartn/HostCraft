using Docker.DotNet;
using Docker.DotNet.Models;
using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using HostCraft.Core.Interfaces;
using Renci.SshNet;
using System.Text;

namespace HostCraft.Infrastructure.Docker;

/// <summary>
/// Implementation of Docker operations using Docker.DotNet.
/// Handles both standalone containers and Swarm services.
/// Uses SSH tunneling for remote Docker connections.
/// </summary>
public class DockerService : IDockerService
{
    private readonly Dictionary<string, DockerClient> _clients = new();
    private readonly Dictionary<string, SshClient> _sshClients = new();
    private readonly Dictionary<string, ForwardedPortDynamic> _sshTunnels = new();
    private readonly Dictionary<string, int> _tunnelPorts = new();
    
    private DockerClient GetClient(Server server)
    {
        var key = $"{server.Host}:{server.Port}";
        
        if (!_clients.ContainsKey(key))
        {
            // For local server, use Unix socket or named pipe
            if (server.Host == "localhost" || server.Host == "127.0.0.1")
            {
                var uri = Environment.OSVersion.Platform == PlatformID.Win32NT
                    ? "npipe://./pipe/docker_engine"
                    : "unix:///var/run/docker.sock";
                
                _clients[key] = new DockerClientConfiguration(new Uri(uri)).CreateClient();
            }
            else
            {
                // For remote servers, execute Docker commands directly via SSH
                // This approach uses SSH.NET to run Docker CLI commands remotely
                // We'll wrap this in a custom DockerClient that executes commands over SSH
                
                // For now, let's try to connect to Docker TCP port if exposed
                // This requires the remote Docker daemon to listen on TCP
                // Users should either expose Docker on TCP or we'll need to wrap all calls
                var dockerUri = $"tcp://{server.Host}:2375";
                _clients[key] = new DockerClientConfiguration(new Uri(dockerUri)).CreateClient();
            }
        }
        
        return _clients[key];
    }
    
    private SshClient GetSshClient(Server server)
    {
        var key = $"{server.Host}:{server.Port}";
        
        if (!_sshClients.ContainsKey(key))
        {
            AuthenticationMethod authMethod;
            
            if (server.PrivateKey != null && !string.IsNullOrEmpty(server.PrivateKey.KeyData))
            {
                // Use private key authentication
                var keyFile = new PrivateKeyFile(new MemoryStream(Encoding.UTF8.GetBytes(server.PrivateKey.KeyData)));
                authMethod = new PrivateKeyAuthenticationMethod(server.Username, keyFile);
            }
            else
            {
                throw new InvalidOperationException($"No private key configured for server {server.Name}");
            }
            
            var connectionInfo = new ConnectionInfo(server.Host, server.Port, server.Username, authMethod);
            var sshClient = new SshClient(connectionInfo);
            
            try
            {
                sshClient.Connect();
                _sshClients[key] = sshClient;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to establish SSH connection to {server.Host}:{server.Port}: {ex.Message}", ex);
            }
        }
        
        return _sshClients[key];
    }
    
    private static int GetAvailablePort()
    {
        // Find an available port for SSH tunnel
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
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
            // For local servers, use Docker client
            if (server.Host == "localhost" || server.Host == "127.0.0.1")
            {
                var client = GetClient(server);
                await client.System.PingAsync(cancellationToken);
                return true;
            }
            
            // For remote servers, test SSH connection and Docker via SSH command
            Console.WriteLine($"[DockerService] Validating connection to {server.Host}:{server.Port}");
            
            var sshClient = GetSshClient(server);
            
            if (!sshClient.IsConnected)
            {
                Console.WriteLine($"[DockerService] SSH client not connected for {server.Host}");
                return false;
            }
            
            Console.WriteLine($"[DockerService] SSH connected to {server.Host}, testing Docker...");
            
            // Test Docker by running 'docker info' command via SSH
            var command = sshClient.CreateCommand("docker info");
            var result = await Task.Run(() => command.Execute(), cancellationToken);
            
            Console.WriteLine($"[DockerService] Docker command exit status: {command.ExitStatus}");
            Console.WriteLine($"[DockerService] Docker command output length: {result?.Length ?? 0}");
            
            if (!string.IsNullOrEmpty(command.Error))
            {
                Console.WriteLine($"[DockerService] Docker command error: {command.Error}");
            }
            
            // Check if command executed successfully (exit status 0)
            return command.ExitStatus == 0 && !string.IsNullOrEmpty(result);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DockerService] Validation exception for {server.Host}: {ex.Message}");
            Console.WriteLine($"[DockerService] Exception type: {ex.GetType().Name}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"[DockerService] Inner exception: {ex.InnerException.Message}");
            }
            // Re-throw the exception so the controller can return a proper error message
            throw;
        }
    }
    
    public async Task<SystemInfo> GetSystemInfoAsync(Server server, CancellationToken cancellationToken = default)
    {
        // For local servers, use Docker client
        if (server.Host == "localhost" || server.Host == "127.0.0.1")
        {
            var client = GetClient(server);
            var info = await client.System.GetSystemInfoAsync(cancellationToken);
            
            return new SystemInfo(
                info.OperatingSystem,
                info.Architecture,
                info.Swarm?.LocalNodeState == "active",
                info.ServerVersion);
        }
        
        // For remote servers, use SSH to get Docker info
        var sshClient = GetSshClient(server);
        var command = sshClient.CreateCommand("docker info --format '{{.OperatingSystem}}|{{.Architecture}}|{{.Swarm.LocalNodeState}}|{{.ServerVersion}}'");
        var result = await Task.Run(() => command.Execute(), cancellationToken);
        
        var parts = result.Trim().Split('|');
        
        return new SystemInfo(
            parts.Length > 0 ? parts[0] : "Unknown",
            parts.Length > 1 ? parts[1] : "Unknown",
            parts.Length > 2 && parts[2] == "active",
            parts.Length > 3 ? parts[3] : "Unknown");
    }
    
    // Cleanup method to dispose SSH connections and tunnels
    public void Dispose()
    {
        foreach (var tunnel in _sshTunnels.Values)
        {
            try
            {
                tunnel.Stop();
            }
            catch { }
        }
        _sshTunnels.Clear();
        
        foreach (var sshClient in _sshClients.Values)
        {
            try
            {
                if (sshClient.IsConnected)
                {
                    sshClient.Disconnect();
                }
                sshClient.Dispose();
            }
            catch { }
        }
        _sshClients.Clear();
        
        foreach (var client in _clients.Values)
        {
            try
            {
                client.Dispose();
            }
            catch { }
        }
        _clients.Clear();
    }
}
