using Docker.DotNet;
using Docker.DotNet.Models;
using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using HostCraft.Core.Interfaces;
using Renci.SshNet;
using System.Text;
using System.Linq;
using System.Text.Json;

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

    public DockerClient GetDockerClient(Server server) => GetClient(server);

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

        if (isInContainer)
        {
            if (server.Host == "localhost" ||
                server.Host == "127.0.0.1" ||
                server.Host == "::1")
            {
                return true;
            }

            return false;
        }

        if (server.Host == "localhost" ||
            server.Host == "127.0.0.1" ||
            server.Host == "::1" ||
            server.Host == "0.0.0.0")
        {
            return true;
        }

        try
        {
            var localHostName = System.Net.Dns.GetHostName();
            if (string.Equals(server.Host, localHostName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var hostEntry = System.Net.Dns.GetHostEntry(server.Host);
            var localAddresses = System.Net.Dns.GetHostAddresses(System.Net.Dns.GetHostName());
            return hostEntry.AddressList.Any(addr => localAddresses.Any(local => local.Equals(addr)));
        }
        catch
        {
            return false;
        }
    }

    private static AuthConfig? ToAuthConfig(RegistryAuthConfig? registryAuth)
    {
        if (registryAuth == null || string.IsNullOrWhiteSpace(registryAuth.ServerAddress))
        {
            return null;
        }

        return new AuthConfig
        {
            ServerAddress = registryAuth.ServerAddress,
            Username = registryAuth.Username,
            Password = registryAuth.Password
        };
    }

    private static string? EncodeRegistryAuth(AuthConfig? authConfig)
    {
        if (authConfig == null)
        {
            return null;
        }

        var payload = JsonSerializer.Serialize(authConfig);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
    }

    private static void ApplyRegistryAuth(object target, RegistryAuthConfig? registryAuth)
    {
        var authConfig = ToAuthConfig(registryAuth);
        if (authConfig == null)
        {
            return;
        }

        var encoded = EncodeRegistryAuth(authConfig);
        var targetType = target.GetType();

        // Try to set Auth/RegistryAuth property if it exists
        var authProperty = targetType.GetProperty("Auth") ?? targetType.GetProperty("AuthConfig") ?? targetType.GetProperty("RegistryAuthConfig");
        if (authProperty != null && authProperty.CanWrite && authProperty.PropertyType.IsInstanceOfType(authConfig))
        {
            authProperty.SetValue(target, authConfig);
        }

        // Try to set encoded header property if available (used by Swarm service create/update)
        var encodedProperty = targetType.GetProperty("EncodedRegistryAuth")
                               ?? targetType.GetProperty("RegistryAuth")
                               ?? targetType.GetProperty("RegistryAuth64");
        if (encodedProperty != null && encodedProperty.CanWrite && encodedProperty.PropertyType == typeof(string))
        {
            encodedProperty.SetValue(target, encoded);
        }

        // Hint swarm to read auth from the spec if the SDK exposes it
        var registryAuthFromProperty = targetType.GetProperty("RegistryAuthFrom");
        if (registryAuthFromProperty != null && registryAuthFromProperty.CanWrite)
        {
            if (registryAuthFromProperty.PropertyType.IsEnum)
            {
                var specValue = Enum.GetValues(registryAuthFromProperty.PropertyType)
                    .Cast<object?>()
                    .FirstOrDefault(v => string.Equals(v?.ToString(), "spec", StringComparison.OrdinalIgnoreCase));
                if (specValue != null)
                {
                    registryAuthFromProperty.SetValue(target, specValue);
                }
            }
            else if (registryAuthFromProperty.PropertyType == typeof(string))
            {
                registryAuthFromProperty.SetValue(target, "spec");
            }
        }
    }
    
    // Container operations
    public async Task<string> CreateContainerAsync(Server server, CreateContainerRequest request, CancellationToken cancellationToken = default)
    {
        var client = GetClient(server);

        // NOTE ON VOLUME PERMISSIONS:
        // Docker named volumes are created with root:root ownership when first accessed.
        // For containers running as non-root users (common for security):
        // 1. Use env vars to specify subdirectories the container can create (e.g., PGDATA=/path/to/volume/subdir)
        // 2. Official images (postgres, mysql, mongo) handle this via entrypoint scripts
        // 3. Custom apps may need documentation on handling volume permissions
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
                Binds = request.Volumes?.Select(kv => $"{kv.Key}:{kv.Value}").ToList(),
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

        return containers.Select(c =>
        {
            var createdAt = c.Created;
            var publishedPorts = c.Ports?
                .Where(p => p.PublicPort > 0)
                .Select(p => new ContainerPortInfo(p.PrivatePort, p.PublicPort, p.Type ?? "tcp", p.IP))
                .ToList()
                ?? new List<ContainerPortInfo>();

            return new ContainerInfo(
                c.ID,
                c.Names.FirstOrDefault()?.TrimStart('/') ?? "unknown",
                c.Image,
                c.State,
                createdAt,
                publishedPorts);
        });
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

        // NOTE ON VOLUME PERMISSIONS:
        // Docker named volumes are created with root:root ownership when first accessed.
        // For services running as non-root users (common for security):
        // 1. Use env vars to specify subdirectories the container can create (e.g., PGDATA=/path/to/volume/subdir)
        // 2. Official images (postgres, mysql, mongo) handle this via entrypoint scripts
        // 3. Swarm services may need placement constraints to ensure volume affinity

        // Resolve network names to IDs for Swarm
        List<NetworkAttachmentConfig>? networkConfigs = null;
        if (request.Networks != null && request.Networks.Count > 0)
        {
            networkConfigs = new List<NetworkAttachmentConfig>();
            foreach (var networkName in request.Networks)
            {
                // Ensure network exists (overlay for swarm) and attach by ID
                var networkId = await EnsureNetworkExistsAsync(server, networkName, cancellationToken);
                networkConfigs.Add(new NetworkAttachmentConfig { Target = networkId });
            }
        }

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
                        // Container labels are for metadata only, not for Traefik routing in Swarm mode
                        Labels = new Dictionary<string, string>(),
                        Mounts = request.Mounts?.Select(kv => new Mount
                        {
                            Source = kv.Key,
                            Target = kv.Value,
                            Type = "volume"
                        }).ToList(),
                        // Health check configuration (required for start-first zero-downtime updates)
                        Healthcheck = request.HealthCheck != null ? new HealthConfig
                        {
                            Test = request.HealthCheck.Test,
                            Interval = TimeSpan.FromSeconds(request.HealthCheck.IntervalSeconds),
                            Timeout = TimeSpan.FromSeconds(request.HealthCheck.TimeoutSeconds),
                            Retries = request.HealthCheck.Retries,
                            StartPeriod = request.HealthCheck.StartPeriodSeconds * 1_000_000_000L // StartPeriod is long (nanoseconds)
                        } : null
                    },
                    Resources = CreateResourceRequirements(request.MemoryLimit, request.CpuLimit),
                    RestartPolicy = new SwarmRestartPolicy
                    {
                        Condition = "on-failure",
                        MaxAttempts = 3
                    },
                    Networks = networkConfigs,
                    Placement = BuildPlacementConfig(request.PlacementConfig)
                },
                Mode = new ServiceMode
                {
                    Replicated = new ReplicatedService { Replicas = (ulong)request.Replicas }
                },
                // Update configuration for zero-downtime rolling deploys (HA/DR default)
                UpdateConfig = new SwarmUpdateConfig
                {
                    // CRITICAL: start-first = zero downtime (start new replicas before stopping old)
                    Order = request.UpdateConfig?.Order ?? "start-first",
                    Parallelism = request.UpdateConfig?.Parallelism ?? 1,
                    Delay = (request.UpdateConfig?.DelaySeconds ?? 10) * 1_000_000_000L, // Convert seconds to nanoseconds
                    FailureAction = request.UpdateConfig?.FailureAction ?? "rollback",
                    MaxFailureRatio = request.UpdateConfig?.MaxFailureRatio ?? 0.0f
                },
                // Rollback configuration for automatic recovery on failed updates
                // Docker.DotNet uses SwarmUpdateConfig for both update and rollback
                RollbackConfig = request.RollbackConfig != null ? new SwarmUpdateConfig
                {
                    Parallelism = request.RollbackConfig.Parallelism,
                    Delay = request.RollbackConfig.DelaySeconds * 1_000_000_000L, // Convert seconds to nanoseconds (faster recovery)
                    FailureAction = request.RollbackConfig.FailureAction,
                    MaxFailureRatio = request.RollbackConfig.MaxFailureRatio
                } : new SwarmUpdateConfig
                {
                    Parallelism = 1,
                    Delay = 5_000_000_000, // 5 seconds default
                    FailureAction = "pause",
                    MaxFailureRatio = 0.0f
                },
                // CRITICAL: In Swarm mode, Traefik reads labels from ServiceSpec.Labels, NOT ContainerSpec.Labels
                Labels = request.Labels ?? new Dictionary<string, string>()
            }
        };

        ApplyRegistryAuth(serviceSpec, request.RegistryAuth);

        // Add port publishing from port mappings or legacy single port
        var portConfigs = new List<PortConfig>();

        if (request.PortMappings != null && request.PortMappings.Count > 0)
        {
            // Use multiple port mappings
            foreach (var mapping in request.PortMappings)
            {
                portConfigs.Add(new PortConfig
                {
                    TargetPort = (uint)mapping.ContainerPort,
                    PublishedPort = (uint)mapping.HostPort,
                    Protocol = mapping.Protocol.ToLowerInvariant(),
                    PublishMode = "ingress"
                });
            }

            // Add Traefik label for the first port mapping
            var primaryPort = request.PortMappings.First();
            serviceSpec.Service.Labels[$"traefik.http.services.{request.Name}.loadbalancer.server.port"] = primaryPort.ContainerPort.ToString();
        }
        else if (request.Port.HasValue)
        {
            // Legacy single port support
            portConfigs.Add(new PortConfig
            {
                TargetPort = (uint)request.Port.Value,
                PublishedPort = (uint)request.Port.Value,
                Protocol = "tcp",
                PublishMode = "ingress"
            });

            // Also add Traefik labels for reverse proxy
            serviceSpec.Service.Labels[$"traefik.http.services.{request.Name}.loadbalancer.server.port"] = request.Port.Value.ToString();
        }

        if (portConfigs.Count > 0)
        {
            serviceSpec.Service.EndpointSpec = new EndpointSpec
            {
                Ports = portConfigs
            };
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
        
        // CRITICAL: Update service labels (Traefik routing) - this enables domain/routing changes
        if (request.Labels != null)
        {
            spec.Labels = request.Labels;
        }

        // Update networks if provided - merge with existing to avoid dropping project networks
        if (request.Networks != null && request.Networks.Count > 0)
        {
            var existingNetworkIds = spec.TaskTemplate.Networks?
                .Select(n => n.Target)
                .Where(t => !string.IsNullOrEmpty(t))
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var networkName in request.Networks)
            {
                var networkId = await EnsureNetworkExistsAsync(server, networkName, cancellationToken);
                existingNetworkIds.Add(networkId);
            }

            spec.TaskTemplate.Networks = existingNetworkIds
                .Select(id => new NetworkAttachmentConfig { Target = id })
                .ToList();
        }

        // Enforce placement spread so new replicas balance across swarm nodes (HA/DR default)
        spec.TaskTemplate.Placement ??= new Placement();
        if (spec.TaskTemplate.Placement.Preferences == null || spec.TaskTemplate.Placement.Preferences.Count == 0)
        {
            spec.TaskTemplate.Placement.Preferences = new List<global::Docker.DotNet.Models.PlacementPreference>
            {
                new global::Docker.DotNet.Models.PlacementPreference
                {
                    Spread = new SpreadOver
                    {
                        SpreadDescriptor = "node.id"
                    }
                }
            };
        }

        // CRITICAL: Apply rolling update configuration for zero-downtime deployments
        if (request.UpdateConfig != null)
        {
            spec.UpdateConfig = new SwarmUpdateConfig
            {
                Order = request.UpdateConfig.Order,
                Parallelism = request.UpdateConfig.Parallelism,
                Delay = request.UpdateConfig.DelaySeconds * 1_000_000_000L,
                FailureAction = request.UpdateConfig.FailureAction,
                MaxFailureRatio = request.UpdateConfig.MaxFailureRatio
            };
        }

        // Apply rollback configuration
        if (request.RollbackConfig != null)
        {
            spec.RollbackConfig = new SwarmUpdateConfig
            {
                Parallelism = request.RollbackConfig.Parallelism,
                Delay = request.RollbackConfig.DelaySeconds * 1_000_000_000L,
                FailureAction = request.RollbackConfig.FailureAction,
                MaxFailureRatio = request.RollbackConfig.MaxFailureRatio
            };
        }

        // Apply health check configuration
        if (request.HealthCheck != null)
        {
            spec.TaskTemplate.ContainerSpec.Healthcheck = new HealthConfig
            {
                Test = request.HealthCheck.Test,
                Interval = TimeSpan.FromSeconds(request.HealthCheck.IntervalSeconds),
                Timeout = TimeSpan.FromSeconds(request.HealthCheck.TimeoutSeconds),
                Retries = request.HealthCheck.Retries,
                StartPeriod = request.HealthCheck.StartPeriodSeconds * 1_000_000_000L
            };
        }

        var updateParameters = new ServiceUpdateParameters
        {
            Service = spec,
            Version = Convert.ToInt64(service.Version.Index)
        };

        ApplyRegistryAuth(updateParameters, request.RegistryAuth);

        await client.Swarm.UpdateServiceAsync(serviceId, updateParameters, cancellationToken);
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
            s.CreatedAt,
            s.Endpoint?.Ports?
                .Where(p => p.PublishedPort > 0)
                .Select(p => new ServicePublishedPort(
                    (int)p.PublishedPort,
                    (int)p.TargetPort,
                    p.Protocol ?? "tcp",
                    p.PublishMode ?? "ingress"))
                .ToList()
                ?? new List<ServicePublishedPort>()));
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

    public async Task<IEnumerable<Core.Interfaces.ServiceTaskInfo>> ListServiceTasksAsync(Server server, string serviceId, CancellationToken cancellationToken = default)
    {
        var client = GetClient(server);

        var filters = new Dictionary<string, IDictionary<string, bool>>
        {
            ["service"] = new Dictionary<string, bool> { [serviceId] = true }
        };

        var tasks = await client.Tasks.ListAsync(new TasksListParameters
        {
            Filters = filters
        }, cancellationToken);

        return tasks.Select(t => new Core.Interfaces.ServiceTaskInfo(
            t.ID,
            t.NodeID ?? string.Empty,
            t.DesiredState.ToString(),
            t.Status != null ? t.Status.State.ToString() : null,
            t.Status?.Err,
            (int)t.Slot,
            t.Status?.Timestamp)).ToList();
    }

    public async Task<IReadOnlyList<ServiceTaskContainerRef>> ListServiceTaskContainersAsync(Server server, string serviceId, CancellationToken cancellationToken = default)
    {
        var client = GetClient(server);

        var filters = new Dictionary<string, IDictionary<string, bool>>
        {
            ["service"] = new Dictionary<string, bool> { [serviceId] = true }
        };

        var tasks = await client.Tasks.ListAsync(new TasksListParameters
        {
            Filters = filters
        }, cancellationToken);

        // Resolve node names for friendlier display
        var nodes = await client.Swarm.ListNodesAsync(cancellationToken: cancellationToken);
        var nodeLookup = nodes.ToDictionary(
            n => n.ID,
            n => n.Description?.Hostname ?? "unknown",
            StringComparer.OrdinalIgnoreCase);

        var results = new List<ServiceTaskContainerRef>();

        foreach (var task in tasks)
        {
            var containerId = task.Status?.ContainerStatus?.ContainerID;
            nodeLookup.TryGetValue(task.NodeID ?? string.Empty, out var nodeName);

            results.Add(new ServiceTaskContainerRef(
                task.ID,
                string.IsNullOrWhiteSpace(containerId) ? null : containerId,
                task.NodeID ?? string.Empty,
                nodeName,
                task.DesiredState.ToString(),
                task.Status?.State.ToString(),
                (int)task.Slot,
                task.Status?.Timestamp));
        }

        return results;
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

    public async Task<Stream> GetTaskLogsAsync(Server server, string taskId, CancellationToken cancellationToken = default)
    {
        var client = GetClient(server);
        
        // Get the task details to find the container ID
        var task = await client.Tasks.InspectAsync(taskId, cancellationToken);
        
        if (task == null)
        {
            throw new InvalidOperationException($"Task {taskId} not found");
        }
        
        var containerId = task.Status?.ContainerStatus?.ContainerID;
        
        if (string.IsNullOrEmpty(containerId))
        {
            throw new InvalidOperationException($"Task {taskId} has no associated container");
        }
        
        return await GetContainerLogsAsync(server, containerId, cancellationToken);
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

        // Force overlay for swarm managers even if IsSwarm flag is stale, so services can attach
        var networkType = server.IsSwarm || server.Type == ServerType.SwarmManager
            ? NetworkType.Overlay
            : NetworkType.Bridge;

        return await CreateNetworkAsync(server, new CreateNetworkRequest(networkName, networkType), cancellationToken);
    }

    public async Task<bool> ConnectContainerToNetworkAsync(Server server, string containerId, string networkName, CancellationToken cancellationToken = default)
    {
        var client = GetClient(server);

        // First ensure the network exists
        var network = await GetNetworkByNameAsync(server, networkName, cancellationToken);
        if (network == null)
        {
            // Create the network if it doesn't exist (use overlay for swarm, bridge otherwise)
            var networkType = server.IsSwarm ? NetworkType.Overlay : NetworkType.Bridge;
            await CreateNetworkAsync(server, new CreateNetworkRequest(networkName, networkType, true), cancellationToken);
        }

        // Connect the container to the network
        await client.Networks.ConnectNetworkAsync(networkName, new NetworkConnectParameters
        {
            Container = containerId
        }, cancellationToken);

        return true;
    }

    // Image operations
    public async Task<bool> PullImageAsync(Server server, string imageName, IProgress<string>? progress = null, RegistryAuthConfig? registryAuth = null, CancellationToken cancellationToken = default)
    {
        var client = GetClient(server);
        var authConfig = ToAuthConfig(registryAuth);
        
        await client.Images.CreateImageAsync(
            new ImagesCreateParameters { FromImage = imageName },
            authConfig,
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
            Target = request.Target ?? "",
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

    public async Task TagImageAsync(Server server, string sourceImage, string targetImage, CancellationToken cancellationToken = default)
    {
        var client = GetClient(server);

        // Parse target image into repository and tag
        var parts = targetImage.Split(':');
        var repository = parts[0];
        var tag = parts.Length > 1 ? parts[1] : "latest";

        await client.Images.TagImageAsync(
            sourceImage,
            new ImageTagParameters
            {
                RepositoryName = repository,
                Tag = tag
            },
            cancellationToken);
    }

    public async Task PushImageAsync(Server server, string imageName, IProgress<string>? progress = null, RegistryAuthConfig? registryAuth = null, CancellationToken cancellationToken = default)
    {
        var client = GetClient(server);
        var authConfig = ToAuthConfig(registryAuth);

        // Parse image name into repository and tag
        var parts = imageName.Split(':');
        var repository = parts[0];
        var tag = parts.Length > 1 ? parts[1] : "latest";

        await client.Images.PushImageAsync(
            repository,
            new ImagePushParameters
            {
                Tag = tag
            },
            authConfig,
            new Progress<JSONMessage>(msg =>
            {
                if (!string.IsNullOrEmpty(msg.Status))
                {
                    progress?.Report(msg.Status);
                }
                if (!string.IsNullOrEmpty(msg.ErrorMessage))
                {
                    throw new InvalidOperationException($"Push failed: {msg.ErrorMessage}");
                }
            }),
            cancellationToken);
    }

    public async Task<bool> ImageExistsAsync(Server server, string imageName, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = GetClient(server);
            await client.Images.InspectImageAsync(imageName, cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
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

    public async Task<string?> GetSwarmManagerAddressAsync(Server server, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = GetClient(server);
            var node = await client.Swarm.InspectNodeAsync("self", cancellationToken);

            // Extract manager advertise address (format: "IP:2377")
            var addr = node?.ManagerStatus?.Addr;
            if (string.IsNullOrEmpty(addr))
                return null;

            // Extract IP (remove port)
            var ip = addr.Split(':')[0];
            return ip;
        }
        catch
        {
            return null;
        }
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

        // First, inspect the node to check its current state
        try
        {
            var node = await client.Swarm.InspectNodeAsync(nodeId, cancellationToken);
            var nodeState = node.Status?.State?.ToString()?.ToLower() ?? "unknown";
            Console.WriteLine($"[DockerService] Removing node {nodeId}: State={nodeState}, Role={node.Spec?.Role}, Force={force}");

            // Check if node can be removed (node must be 'down' unless force=true)
            if (nodeState != "down" && !force)
            {
                Console.WriteLine($"[DockerService] Node {nodeId} is not in 'down' state (current: {nodeState}). Use force=true to remove anyway.");
                throw new InvalidOperationException($"Node is not in 'down' state. Current state: {nodeState}. Use force=true to remove.");
            }
        }
        catch (DockerApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Console.WriteLine($"[DockerService] Node {nodeId} not found - may have already been removed");
            return true; // Node doesn't exist, consider it removed
        }

        await client.Swarm.RemoveNodeAsync(nodeId, force, cancellationToken);
        Console.WriteLine($"[DockerService] Successfully removed node {nodeId}");
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

    public async Task<ContainerResourceUsage?> GetContainerStatsAsync(Server server, string containerId, CancellationToken cancellationToken = default)
    {
        var client = GetClient(server);

        ContainerStatsResponse? stats = null;
        var progress = new Progress<ContainerStatsResponse>(response =>
        {
            stats ??= response;
        });

        await client.Containers.GetContainerStatsAsync(containerId, new ContainerStatsParameters
        {
            Stream = false
        }, progress, cancellationToken);

        if (stats == null)
        {
            return null;
        }

        var cpuPercent = CalculateCpuPercent(stats);
        ulong memoryUsageRaw = stats.MemoryStats?.Usage ?? 0;
        ulong memoryLimitRaw = stats.MemoryStats?.Limit ?? 0;
        var memoryUsage = (long)Math.Min(memoryUsageRaw, (ulong)long.MaxValue);
        var memoryLimit = (long)Math.Min(memoryLimitRaw, (ulong)long.MaxValue);
        var memoryPercent = memoryLimit > 0 ? Math.Round((double)memoryUsage / memoryLimit * 100, 2) : 0d;

        var (rxBytes, txBytes) = SumNetworkBytes(stats);
        var (blkRead, blkWrite) = SumBlockIo(stats);

        return new ContainerResourceUsage(
            cpuPercent,
            memoryUsage,
            memoryLimit,
            memoryPercent,
            rxBytes,
            txBytes,
            blkRead,
            blkWrite,
            DateTime.UtcNow);
    }

    private static double CalculateCpuPercent(ContainerStatsResponse stats)
    {
        try
        {
            var cpuTotal = stats.CPUStats?.CPUUsage?.TotalUsage ?? 0;
            var preCpuTotal = stats.PreCPUStats?.CPUUsage?.TotalUsage ?? 0;
            var systemTotal = stats.CPUStats?.SystemUsage ?? 0;
            var preSystemTotal = stats.PreCPUStats?.SystemUsage ?? 0;

            var cpuDelta = (double)(cpuTotal - preCpuTotal);
            var systemDelta = (double)(systemTotal - preSystemTotal);

            if (cpuDelta <= 0 || systemDelta <= 0)
            {
                return 0d;
            }

            var onlineCpus = stats.CPUStats?.OnlineCPUs ?? 0;
            double cpuCount = onlineCpus > 0
                ? onlineCpus
                : stats.CPUStats?.CPUUsage?.PercpuUsage?.Count ?? 1;

            return Math.Round((cpuDelta / systemDelta) * cpuCount * 100, 2);
        }
        catch
        {
            return 0d;
        }
    }

    private static (long rxBytes, long txBytes) SumNetworkBytes(ContainerStatsResponse stats)
    {
        ulong rx = 0;
        ulong tx = 0;

        if (stats.Networks != null)
        {
            foreach (var kvp in stats.Networks)
            {
                var network = kvp.Value;
                if (network == null)
                {
                    continue;
                }

                rx += network.RxBytes;
                tx += network.TxBytes;
            }
        }

        return ((long)Math.Min(rx, (ulong)long.MaxValue), (long)Math.Min(tx, (ulong)long.MaxValue));
    }

    private static (long readBytes, long writeBytes) SumBlockIo(ContainerStatsResponse stats)
    {
        ulong read = 0;
        ulong write = 0;

        var entries = stats.BlkioStats?.IoServiceBytesRecursive;
        if (entries != null)
        {
            foreach (var entry in entries)
            {
                var op = entry.Op?.ToLowerInvariant();
                var value = entry.Value;
                if (op == "read")
                {
                    read += value;
                }
                else if (op == "write")
                {
                    write += value;
                }
            }
        }

        return ((long)Math.Min(read, (ulong)long.MaxValue), (long)Math.Min(write, (ulong)long.MaxValue));
    }
    
    private ResourceRequirements? CreateResourceRequirements(long? memoryLimit, long? cpuLimit)
    {
        // Resource limits not yet implemented - requires Docker.DotNet API investigation
        // The MemoryLimit and CpuLimit parameters are captured in the request for future use
        return null;
    }

    private Placement BuildPlacementConfig(ServicePlacementConfig? placementConfig)
    {
        var placement = new Placement();

        // Apply placement constraints
        if (placementConfig?.Constraints != null && placementConfig.Constraints.Count > 0)
        {
            placement.Constraints = placementConfig.Constraints;
        }

        // Apply placement preferences for replica distribution
        if (placementConfig?.Preferences != null && placementConfig.Preferences.Count > 0)
        {
            placement.Preferences = placementConfig.Preferences
                .Select(p => new global::Docker.DotNet.Models.PlacementPreference
                {
                    Spread = new SpreadOver { SpreadDescriptor = p.Spread }
                })
                .ToList();
        }
        else
        {
            // Default: Spread replicas evenly across nodes for HA/DR
            placement.Preferences = new List<global::Docker.DotNet.Models.PlacementPreference>
            {
                new global::Docker.DotNet.Models.PlacementPreference
                {
                    Spread = new SpreadOver { SpreadDescriptor = "node.id" }
                }
            };
        }

        // Apply max replicas per node
        if (placementConfig?.MaxReplicasPerNode != null)
        {
            placement.MaxReplicas = placementConfig.MaxReplicasPerNode.Value;
        }

        return placement;
    }

    // Volume operations
    public async Task<string> CreateVolumeAsync(Server server, VolumeCreateRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = GetClient(server);

            var createParams = new VolumesCreateParameters
            {
                Name = request.Name,
                Driver = request.Driver,
                DriverOpts = request.DriverOpts,
                Labels = request.Labels
            };

            var response = await client.Volumes.CreateAsync(createParams, cancellationToken);
            return response.Name;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to create volume {request.Name}: {ex.Message}", ex);
        }
    }

    public async Task<bool> RemoveVolumeAsync(Server server, string volumeName, bool force = false, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = GetClient(server);
            await client.Volumes.RemoveAsync(volumeName, force, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to remove volume {volumeName}: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> VolumeExistsAsync(Server server, string volumeName, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = GetClient(server);
            await client.Volumes.InspectAsync(volumeName, cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<IEnumerable<VolumeInfo>> ListVolumesAsync(Server server, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = GetClient(server);
            var response = await client.Volumes.ListAsync(cancellationToken);

            return response.Volumes?.Select(v => new VolumeInfo(
                Name: v.Name,
                Driver: v.Driver,
                MountPoint: v.Mountpoint,
                Labels: v.Labels != null ? new Dictionary<string, string>(v.Labels) : null
            )).ToList() ?? new List<VolumeInfo>();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to list volumes: {ex.Message}", ex);
        }
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
