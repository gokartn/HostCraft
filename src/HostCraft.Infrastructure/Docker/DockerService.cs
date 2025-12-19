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
public class DockerService : IDockerService, IDisposable
{
    private readonly Dictionary<string, DockerClient> _clients = new();
    private readonly Dictionary<string, SshClient> _sshClients = new();
    private readonly Dictionary<string, Renci.SshNet.ForwardedPortLocal> _sshTunnels = new();
    private readonly Dictionary<string, int> _tunnelPorts = new();
    
    private DockerClient GetClient(Server server)
    {
        var key = $"{server.Host}:{server.Port}";
        
        if (!_clients.ContainsKey(key))
        {
            // For local server, use Unix socket or named pipe directly (no SSH needed)
            if (IsLocalhostServer(server))
            {
                var uri = Environment.OSVersion.Platform == PlatformID.Win32NT
                    ? "npipe://./pipe/docker_engine"
                    : "unix:///var/run/docker.sock";
                
                _clients[key] = new DockerClientConfiguration(new Uri(uri)).CreateClient();
            }
            else
            {
                // For remote servers, create SSH tunnel to Docker socket
                // We use socat on the remote server to expose the Unix socket on a TCP port
                // Then forward that TCP port through SSH to our local machine
                
                var sshClient = GetSshClient(server);
                
                // Check if socat is installed, if not try to install it
                var checkSocat = sshClient.CreateCommand("which socat || command -v socat");
                var socatPath = checkSocat.Execute().Trim();
                
                if (string.IsNullOrEmpty(socatPath))
                {
                    // Try to install socat (works on Ubuntu/Debian)
                    var installCmd = sshClient.CreateCommand("sudo apt-get update && sudo DEBIAN_FRONTEND=noninteractive apt-get install -y socat");
                    installCmd.Execute();
                    
                    // Verify installation
                    socatPath = sshClient.CreateCommand("which socat").Execute().Trim();
                    if (string.IsNullOrEmpty(socatPath))
                    {
                        throw new InvalidOperationException("socat is not installed on the remote server and automatic installation failed. Please install it manually: sudo apt-get install socat");
                    }
                }
                
                // Find an available port on the remote server for socat
                var remotePort = 2376; // Use 2376 (Docker TLS port) as it's usually available
                
                // Kill any existing socat on this port
                var killSocat = sshClient.CreateCommand($"pkill -f 'socat.*:{remotePort}'");
                killSocat.Execute();
                
                // Start socat on remote server to bridge Unix socket to TCP
                // This runs in background and will be cleaned up when SSH session ends
                var socatCommand = $"nohup socat TCP-LISTEN:{remotePort},reuseaddr,fork UNIX-CONNECT:/var/run/docker.sock > /dev/null 2>&1 & echo $!";
                var socatPidCmd = sshClient.CreateCommand(socatCommand);
                var socatPid = socatPidCmd.Execute().Trim();
                
                // Give socat a moment to start
                System.Threading.Thread.Sleep(1000);
                
                // Get an available local port for the SSH tunnel
                var localPort = GetAvailablePort();
                _tunnelPorts[key] = localPort;
                
                // Create SSH port forward from local port to remote socat port
                var forwardedPort = new Renci.SshNet.ForwardedPortLocal("127.0.0.1", (uint)localPort, "127.0.0.1", (uint)remotePort);
                sshClient.AddForwardedPort(forwardedPort);
                forwardedPort.Start();
                _sshTunnels[key] = forwardedPort;
                
                // Connect to Docker via the SSH tunnel
                var dockerUri = $"tcp://127.0.0.1:{localPort}";
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
    
    private static bool IsLocalhostServer(Server server)
    {
        // IMPORTANT: When running in Docker, "localhost" means the CONTAINER, not the HOST
        // To access the host's Docker daemon:
        // 1. Mount /var/run/docker.sock from host into container
        // 2. Use unix:///var/run/docker.sock which connects to the HOST's Docker
        // The mounted socket from the host will show the HOST's Docker info (including swarm)
        
        // Check if we're running inside a Docker container
        bool isInContainer = false;
        try
        {
            isInContainer = File.Exists("/.dockerenv") || 
                           (File.Exists("/proc/self/cgroup") && 
                            File.ReadAllText("/proc/self/cgroup").Contains("docker"));
        }
        catch
        {
            // If we can't check, assume not in container
            isInContainer = false;
        }
        
        // When in container with mounted Docker socket, "localhost" accesses the HOST's Docker
        // This is what we want - the mounted socket connects to the host's daemon
        if (isInContainer)
        {
            // In container: "localhost" via mounted socket = HOST's Docker daemon
            if (server.Host == "localhost" || 
                server.Host == "127.0.0.1" || 
                server.Host == "::1")
            {
                return true;
            }
            // Don't check hostname in container - it's the container's name, not the host
            return false;
        }
        
        // Not in container - check common localhost identifiers
        if (server.Host == "localhost" || 
            server.Host == "127.0.0.1" || 
            server.Host == "::1" ||
            server.Host == "0.0.0.0")
        {
            return true;
        }
        
        // Check if it matches the local machine name
        try
        {
            var localHostName = System.Net.Dns.GetHostName();
            if (string.Equals(server.Host, localHostName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            
            // Check if the host resolves to a local IP
            var hostEntry = System.Net.Dns.GetHostEntry(server.Host);
            var localAddresses = System.Net.Dns.GetHostEntry(localHostName).AddressList;
            
            foreach (var addr in hostEntry.AddressList)
            {
                if (System.Net.IPAddress.IsLoopback(addr) || localAddresses.Any(la => la.Equals(addr)))
                {
                    return true;
                }
            }
        }
        catch
        {
            // If DNS resolution fails, just use the basic check
        }
        
        return false;
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
        var client = GetClient(server);
        
        var logsParams = new ContainerLogsParameters
        {
            ShowStdout = true,
            ShowStderr = true,
            Follow = false,
            Timestamps = true,
            Tail = "500" // Get last 500 lines
        };
        
#pragma warning disable CS0618 // Type or member is obsolete - we need Stream return type for interface
        return await client.Containers.GetContainerLogsAsync(containerId, logsParams, cancellationToken);
#pragma warning restore CS0618
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
                    Resources = CreateResourceRequirements(request.MemoryLimit, request.CpuLimit),
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

    public async Task<bool> RollbackServiceAsync(Server server, string serviceId, CancellationToken cancellationToken = default)
    {
        var client = GetClient(server);
        var service = await client.Swarm.InspectServiceAsync(serviceId, cancellationToken);

        // Check if there is a previous spec to rollback to
        if (service.PreviousSpec == null)
        {
            // No previous spec available - cannot rollback
            return false;
        }

        // Update the service with the previous spec, triggering a rollback
        var updateParams = new ServiceUpdateParameters
        {
            Service = service.PreviousSpec,
            Version = Convert.ToInt64(service.Version.Index),
            Rollback = "previous" // Signal Docker to use rollback logic
        };

        await client.Swarm.UpdateServiceAsync(serviceId, updateParams, cancellationToken);

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
            new ServiceLogsParameters 
            { 
                ShowStdout = true, 
                ShowStderr = true, 
                Follow = false,
                Timestamps = true,
                Tail = "500" // Get last 500 lines
            },
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

        // Create tar archive of build context
        var tarStream = await CreateBuildContextTarAsync(request.Context);

        var buildParameters = new ImageBuildParameters
        {
            Tags = new List<string> { request.Tag },
            Dockerfile = request.Dockerfile,
            BuildArgs = request.BuildArgs,
            NoCache = false,
            Remove = true,
            ForceRemove = true
        };

        var imageId = "";
        await client.Images.BuildImageFromDockerfileAsync(
            buildParameters,
            tarStream,
            null, // auth config
            null, // headers
            new Progress<JSONMessage>(msg =>
            {
                if (!string.IsNullOrEmpty(msg.Stream))
                {
                    progress?.Report(msg.Stream.TrimEnd('\n'));
                }
                if (!string.IsNullOrEmpty(msg.ID))
                {
                    imageId = msg.ID;
                }
                if (!string.IsNullOrEmpty(msg.ErrorMessage))
                {
                    throw new InvalidOperationException($"Build failed: {msg.ErrorMessage}");
                }
            }),
            cancellationToken);

        return string.IsNullOrEmpty(imageId) ? request.Tag : imageId;
    }

    private async Task<Stream> CreateBuildContextTarAsync(string sourceDirectory)
    {
        var tarStream = new MemoryStream();

        // Load .dockerignore patterns
        var ignorePatterns = new List<string> { ".git" };
        var dockerIgnorePath = Path.Combine(sourceDirectory, ".dockerignore");
        if (File.Exists(dockerIgnorePath))
        {
            var lines = await File.ReadAllLinesAsync(dockerIgnorePath);
            ignorePatterns.AddRange(lines.Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#")).Select(l => l.Trim()));
        }

        // Get all files
        var files = Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories)
            .Where(f => !ShouldIgnoreFile(Path.GetRelativePath(sourceDirectory, f).Replace('\\', '/'), ignorePatterns))
            .ToList();

        // Write tar archive
        foreach (var filePath in files)
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, filePath).Replace('\\', '/');
            var fileContent = await File.ReadAllBytesAsync(filePath);
            var fileInfo = new FileInfo(filePath);

            // Write header
            var header = CreateTarHeader(relativePath, fileContent.Length, fileInfo.LastWriteTimeUtc);
            await tarStream.WriteAsync(header, 0, 512);

            // Write content
            await tarStream.WriteAsync(fileContent, 0, fileContent.Length);

            // Pad to 512 bytes
            var padding = 512 - (fileContent.Length % 512);
            if (padding < 512)
                await tarStream.WriteAsync(new byte[padding], 0, padding);
        }

        // End of archive
        await tarStream.WriteAsync(new byte[1024], 0, 1024);
        tarStream.Position = 0;
        return tarStream;
    }

    private bool ShouldIgnoreFile(string relativePath, List<string> patterns)
    {
        foreach (var pattern in patterns)
        {
            if (pattern.EndsWith("/") && relativePath.StartsWith(pattern.TrimEnd('/')))
                return true;
            if (relativePath == pattern || relativePath.StartsWith(pattern + "/"))
                return true;
        }
        return false;
    }

    private byte[] CreateTarHeader(string fileName, long fileSize, DateTime modTime)
    {
        var header = new byte[512];
        var nameBytes = System.Text.Encoding.ASCII.GetBytes(fileName.Length > 100 ? fileName.Substring(0, 100) : fileName);
        Array.Copy(nameBytes, 0, header, 0, nameBytes.Length);

        Array.Copy(System.Text.Encoding.ASCII.GetBytes("0000644\0"), 0, header, 100, 8); // mode
        Array.Copy(System.Text.Encoding.ASCII.GetBytes("0000000\0"), 0, header, 108, 8); // uid
        Array.Copy(System.Text.Encoding.ASCII.GetBytes("0000000\0"), 0, header, 116, 8); // gid

        var sizeOctal = Convert.ToString(fileSize, 8).PadLeft(11, '0') + "\0";
        Array.Copy(System.Text.Encoding.ASCII.GetBytes(sizeOctal), 0, header, 124, 12);

        var mtime = (long)(modTime - new DateTime(1970, 1, 1)).TotalSeconds;
        var mtimeOctal = Convert.ToString(mtime, 8).PadLeft(11, '0') + "\0";
        Array.Copy(System.Text.Encoding.ASCII.GetBytes(mtimeOctal), 0, header, 136, 12);

        for (int i = 148; i < 156; i++) header[i] = 0x20;
        header[156] = (byte)'0';

        Array.Copy(System.Text.Encoding.ASCII.GetBytes("ustar\0"), 0, header, 257, 6);
        header[263] = (byte)'0';
        header[264] = (byte)'0';

        int checksum = 0;
        for (int i = 0; i < 512; i++) checksum += header[i];
        var checksumOctal = Convert.ToString(checksum, 8).PadLeft(6, '0') + "\0 ";
        Array.Copy(System.Text.Encoding.ASCII.GetBytes(checksumOctal), 0, header, 148, 8);

        return header;
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
    
    public async Task<bool> IsSwarmActiveAsync(Server server, CancellationToken cancellationToken = default)
    {
        var info = await GetSystemInfoAsync(server, cancellationToken);
        return info.SwarmActive;
    }
    
    // Swarm node management
    public async Task<IEnumerable<NodeInfo>> ListNodesAsync(Server server, CancellationToken cancellationToken = default)
    {
        var client = GetClient(server);
        var nodes = await client.Swarm.ListNodesAsync(cancellationToken: cancellationToken);
        
        return nodes.Select(n => new NodeInfo(
            n.ID,
            n.Description?.Hostname ?? "Unknown",
            n.Spec?.Role ?? "unknown",
            n.Status?.State?.ToString() ?? "unknown",
            n.Spec?.Availability ?? "unknown",
            n.ManagerStatus?.Leader ?? false,
            n.Status?.Addr ?? "unknown",
            n.Description?.Resources?.NanoCPUs ?? 0,
            n.Description?.Resources?.MemoryBytes ?? 0,
            n.Description?.Engine?.EngineVersion ?? "Unknown",
            $"{n.Description?.Platform?.OS}/{n.Description?.Platform?.Architecture}"
        ));
    }
    
    public async Task<NodeInfo?> InspectNodeAsync(Server server, string nodeId, CancellationToken cancellationToken = default)
    {
        var client = GetClient(server);
        var node = await client.Swarm.InspectNodeAsync(nodeId, cancellationToken);
        
        if (node == null)
        {
            return null;
        }
        
        return new NodeInfo(
            node.ID,
            node.Description?.Hostname ?? "Unknown",
            node.Spec?.Role ?? "unknown",
            node.Status?.State?.ToString() ?? "unknown",
            node.Spec?.Availability ?? "unknown",
            node.ManagerStatus?.Leader ?? false,
            node.Status?.Addr ?? "unknown",
            node.Description?.Resources?.NanoCPUs ?? 0,
            node.Description?.Resources?.MemoryBytes ?? 0,
            node.Description?.Engine?.EngineVersion ?? "Unknown",
            $"{node.Description?.Platform?.OS}/{node.Description?.Platform?.Architecture}"
        );
    }
    
    public async Task<bool> UpdateNodeAsync(Server server, string nodeId, NodeUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var client = GetClient(server);
        
        // Get current node spec
        var node = await client.Swarm.InspectNodeAsync(nodeId, cancellationToken);
        if (node == null)
        {
            return false;
        }
        
        // Update the spec with requested changes
        var spec = node.Spec;
        if (request.Role != null)
        {
            spec.Role = request.Role;
        }
        if (request.Availability != null)
        {
            spec.Availability = request.Availability;
        }
        
        await client.Swarm.UpdateNodeAsync(nodeId, node.Version.Index, spec, cancellationToken);
        return true;
    }
    
    public async Task<bool> RemoveNodeAsync(Server server, string nodeId, bool force = false, CancellationToken cancellationToken = default)
    {
        var client = GetClient(server);
        await client.Swarm.RemoveNodeAsync(nodeId, force, cancellationToken);
        return true;
    }
    
    public async Task<(string WorkerToken, string ManagerToken)> GetJoinTokensAsync(Server server, CancellationToken cancellationToken = default)
    {
        var client = GetClient(server);
        var swarm = await client.Swarm.InspectSwarmAsync(cancellationToken);
        
        return (swarm.JoinTokens.Worker, swarm.JoinTokens.Manager);
    }
    
    // Server validation
    public async Task<bool> ValidateConnectionAsync(Server server, CancellationToken cancellationToken = default)
    {
        try
        {
            // For local servers, use Docker client directly (no SSH)
            if (IsLocalhostServer(server))
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
        var isLocalhost = IsLocalhostServer(server);
        Console.WriteLine($"[DockerService.GetSystemInfo] Server: {server.Name} ({server.Host}) - IsLocalhost: {isLocalhost}");

        // For local servers, use Docker client directly (no SSH)
        if (isLocalhost)
        {
            Console.WriteLine($"[DockerService.GetSystemInfo] Using local Docker client for {server.Name}");
            var client = GetClient(server);
            var info = await client.System.GetSystemInfoAsync(cancellationToken);

            var swarmActive = info.Swarm?.LocalNodeState == "active";
            Console.WriteLine($"[DockerService.GetSystemInfo] Docker info retrieved - SwarmNodeState: {info.Swarm?.LocalNodeState}, SwarmActive: {swarmActive}");

            // Get additional swarm info if active
            string? swarmNodeId = null;
            string? swarmId = null;
            string? swarmNodeAddress = null;
            string? swarmNodeState = null;
            string? swarmNodeAvailability = null;
            bool isSwarmManager = false;
            bool isSwarmLeader = false;

            if (swarmActive)
            {
                swarmNodeId = info.Swarm?.NodeID;
                swarmNodeAddress = info.Swarm?.NodeAddr;
                isSwarmManager = info.Swarm?.ControlAvailable ?? false;

                // Get more details about the local node
                try
                {
                    var swarm = await client.Swarm.InspectSwarmAsync(cancellationToken);
                    swarmId = swarm?.ID;

                    if (!string.IsNullOrEmpty(swarmNodeId))
                    {
                        var node = await client.Swarm.InspectNodeAsync(swarmNodeId, cancellationToken);
                        swarmNodeState = node?.Status?.State?.ToString()?.ToLower();
                        swarmNodeAvailability = node?.Spec?.Availability?.ToLower();
                        isSwarmLeader = node?.ManagerStatus?.Leader ?? false;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DockerService.GetSystemInfo] Could not get detailed swarm info: {ex.Message}");
                }
            }

            return new SystemInfo(
                info.OperatingSystem,
                info.Architecture,
                swarmActive,
                info.ServerVersion,
                info.Name, // Hostname
                swarmNodeId,
                swarmId,
                swarmNodeAddress,
                swarmNodeState,
                swarmNodeAvailability,
                isSwarmManager,
                isSwarmLeader);
        }

        // For remote servers, use SSH to get Docker info with extended format
        Console.WriteLine($"[DockerService.GetSystemInfo] Using SSH for remote server {server.Name}");
        var sshClient = GetSshClient(server);
        // Extended format to get more swarm info
        var command = sshClient.CreateCommand(
            "docker info --format '{{.OperatingSystem}}|{{.Architecture}}|{{.Swarm.LocalNodeState}}|{{.ServerVersion}}|{{.Name}}|{{.Swarm.NodeID}}|{{.Swarm.Cluster.ID}}|{{.Swarm.NodeAddr}}|{{.Swarm.ControlAvailable}}'");
        var result = await Task.Run(() => command.Execute(), cancellationToken);

        Console.WriteLine($"[DockerService.GetSystemInfo] SSH result: {result}");
        var parts = result.Trim().Split('|');

        var swarmState = parts.Length > 2 ? parts[2] : "unknown";
        var isSwarmActive = swarmState == "active";
        Console.WriteLine($"[DockerService.GetSystemInfo] Swarm state: {swarmState}, Active: {isSwarmActive}");

        return new SystemInfo(
            parts.Length > 0 ? parts[0] : "Unknown",
            parts.Length > 1 ? parts[1] : "Unknown",
            isSwarmActive,
            parts.Length > 3 ? parts[3] : "Unknown",
            parts.Length > 4 && !string.IsNullOrEmpty(parts[4]) ? parts[4] : null, // Hostname
            parts.Length > 5 && !string.IsNullOrEmpty(parts[5]) ? parts[5] : null, // SwarmNodeId
            parts.Length > 6 && !string.IsNullOrEmpty(parts[6]) ? parts[6] : null, // SwarmId
            parts.Length > 7 && !string.IsNullOrEmpty(parts[7]) ? parts[7] : null, // SwarmNodeAddress
            isSwarmActive ? "ready" : null, // SwarmNodeState (simplified for SSH)
            isSwarmActive ? "active" : null, // SwarmNodeAvailability (simplified for SSH)
            parts.Length > 8 && parts[8].ToLower() == "true", // IsSwarmManager
            false); // IsSwarmLeader (would need additional query)
    }
    
    private ResourceRequirements? CreateResourceRequirements(long? memoryLimit, long? cpuLimit)
    {
        // Resource limits not yet implemented - requires Docker.DotNet API investigation
        // The MemoryLimit and CpuLimit parameters are captured in the request for future use
        return null;
    }

    // Cleanup method to dispose SSH connections and tunnels
    public void Dispose()
    {
        // Stop all SSH port forwarding tunnels
        foreach (var tunnel in _sshTunnels.Values)
        {
            try
            {
                if (tunnel.IsStarted)
                {
                    tunnel.Stop();
                }
            }
            catch (Exception ex) 
            { 
                System.Diagnostics.Debug.WriteLine($"Failed to stop SSH tunnel during disposal: {ex.Message}");
            }
        }
        _sshTunnels.Clear();
        
        // Disconnect and dispose all SSH clients
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
            catch (Exception ex) 
            { 
                System.Diagnostics.Debug.WriteLine($"Failed to disconnect/dispose SSH client during disposal: {ex.Message}");
            }
        }
        _sshClients.Clear();
        
        // Dispose all Docker clients
        foreach (var client in _clients.Values)
        {
            try
            {
                client.Dispose();
            }
            catch (Exception ex) 
            { 
                System.Diagnostics.Debug.WriteLine($"Failed to dispose Docker client during disposal: {ex.Message}");
            }
        }
        _clients.Clear();
    }
}
