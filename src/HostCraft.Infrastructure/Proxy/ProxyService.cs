using System.Text;
using Docker.DotNet;
using Docker.DotNet.Models;
using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using HostCraft.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace HostCraft.Infrastructure.Proxy;

/// <summary>
/// Service for managing reverse proxy configurations (Traefik, Caddy, YARP).
/// </summary>
public class ProxyService : IProxyService
{
    private readonly IDockerService _dockerService;
    private readonly ILogger<ProxyService> _logger;

    public ProxyService(IDockerService dockerService, ILogger<ProxyService> logger)
    {
        _dockerService = dockerService;
        _logger = logger;
    }

    public async Task<bool> ConfigureApplicationAsync(Application application, CancellationToken cancellationToken = default)
    {
        if (application.Server?.ProxyType == ProxyType.None)
        {
            _logger.LogInformation("No proxy configured for application {AppName}", application.Name);
            return true;
        }

        try
        {
            _logger.LogInformation("Configuring {ProxyType} for application {AppName}", 
                application.Server?.ProxyType, application.Name);

            // Ensure proxy is deployed on the server
            if (application.Server != null)
            {
                await EnsureProxyDeployedAsync(application.Server, cancellationToken);
            }

            // Application labels are already set in DockerService.DeployServiceAsync
            // No additional configuration needed here
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to configure proxy for application {AppName}", application.Name);
            return false;
        }
    }

    public async Task<bool> RemoveApplicationAsync(Application application, CancellationToken cancellationToken = default)
    {
        if (application.Server?.ProxyType == ProxyType.None)
            return true;

        try
        {
            _logger.LogInformation("Removing {ProxyType} configuration for application {AppName}", 
                application.Server?.ProxyType, application.Name);

            // Docker service removal automatically removes labels
            // No additional cleanup needed
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove proxy configuration for application {AppName}", application.Name);
            return false;
        }
    }

    public async Task<bool> ReloadConfigurationAsync(Server server, CancellationToken cancellationToken = default)
    {
        if (server.ProxyType == ProxyType.None)
            return true;

        try
        {
            _logger.LogInformation("Reloading {ProxyType} configuration on server {ServerName}", 
                server.ProxyType, server.Name);

            // Traefik and Caddy auto-reload by watching Docker events
            // No manual reload needed
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reload proxy configuration on server {ServerName}", server.Name);
            return false;
        }
    }

    public async Task<string> GenerateConfigAsync(Application application, CancellationToken cancellationToken = default)
    {
        if (application.Server?.ProxyType == null || application.Server.ProxyType == ProxyType.None)
            return "# No proxy configured";

        return application.Server.ProxyType switch
        {
            ProxyType.Traefik => GenerateTraefikConfig(application),
            ProxyType.Caddy => GenerateCaddyConfig(application),
            ProxyType.Nginx => GenerateNginxConfig(application),
            ProxyType.Yarp => GenerateYarpConfig(application),
            _ => "# Unknown proxy type"
        };
    }

    /// <summary>
    /// Ensures the reverse proxy is deployed and running on the server.
    /// </summary>
    public async Task<bool> EnsureProxyDeployedAsync(Server server, CancellationToken cancellationToken = default)
    {
        if (server.ProxyType == ProxyType.None)
            return true;

        try
        {
            // Check if proxy already exists (service or container)
            if (server.IsSwarm)
            {
                var serviceExists = await CheckTraefikServiceExistsAsync(server, cancellationToken);
                if (serviceExists)
                {
                    // Check if Traefik has Let's Encrypt configured
                    if (server.ProxyType == ProxyType.Traefik && !string.IsNullOrWhiteSpace(server.DefaultLetsEncryptEmail))
                    {
                        var hasLetsEncrypt = await CheckTraefikHasLetsEncryptAsync(server, cancellationToken);
                        if (!hasLetsEncrypt)
                        {
                            _logger.LogWarning("Traefik exists but missing Let's Encrypt configuration. Redeploying...");
                            await RemoveTraefikServiceAsync(server, cancellationToken);
                            // Continue to deployment below
                        }
                        else
                        {
                            _logger.LogInformation("{ProxyType} service already deployed on {ServerName}",
                                server.ProxyType, server.Name);
                            return true;
                        }
                    }
                    else
                    {
                        _logger.LogInformation("{ProxyType} service already deployed on {ServerName}",
                            server.ProxyType, server.Name);
                        return true;
                    }
                }
            }
            else
            {
                var proxyName = GetProxyContainerName(server.ProxyType);
                var containers = await _dockerService.ListContainersAsync(server, true, cancellationToken);
                var existingProxy = containers.FirstOrDefault(c => 
                    c.Name.Contains(proxyName, StringComparison.OrdinalIgnoreCase));

                if (existingProxy != null)
                {
                    _logger.LogInformation("{ProxyType} container already deployed on {ServerName}", 
                        server.ProxyType, server.Name);
                    return true;
                }
            }

            _logger.LogInformation("Deploying {ProxyType} on server {ServerName} (Mode: {Mode})", 
                server.ProxyType, server.Name, server.IsSwarm ? "Swarm Service" : "Standalone Container");

            // Route to appropriate deployment method
            if (server.ProxyType == ProxyType.Traefik)
            {
                return server.IsSwarm 
                    ? await DeployTraefikAsSwarmServiceAsync(server, cancellationToken)
                    : await DeployTraefikAsContainerAsync(server, cancellationToken);
            }

            return server.ProxyType switch
            {
                ProxyType.Caddy => await DeployCaddyAsync(server, cancellationToken),
                ProxyType.Nginx => await DeployNginxAsync(server, cancellationToken),
                ProxyType.Yarp => await DeployYarpAsync(server, cancellationToken),
                _ => false
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deploy {ProxyType} on server {ServerName}", 
                server.ProxyType, server.Name);
            return false;
        }
    }

    /// <summary>
    /// Checks if Traefik is already deployed as a swarm service.
    /// </summary>
    private async Task<bool> CheckTraefikServiceExistsAsync(Server server, CancellationToken cancellationToken)
    {
        try
        {
            var services = await _dockerService.ListServicesAsync(server, cancellationToken);
            var traefikService = services.FirstOrDefault(s => 
                s.Name.Contains("traefik", StringComparison.OrdinalIgnoreCase));
            
            if (traefikService == null)
                return false;
            
            // Verify it's HostCraft-managed by inspecting
            var inspected = await _dockerService.InspectServiceAsync(server, traefikService.Id, cancellationToken);
            return inspected != null && 
                   inspected.Labels != null && 
                   inspected.Labels.ContainsKey("hostcraft.proxy") &&
                   inspected.Labels["hostcraft.proxy"] == "traefik";
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to check for existing Traefik service");
            return false;
        }
    }

    /// <summary>
    /// Deploys Traefik as a Docker Swarm service for High Availability.
    /// Runs 2 replicas on manager nodes with automatic failover.
    /// Uses direct Docker.DotNet API for full control over service configuration.
    /// </summary>
    private async Task<bool> DeployTraefikAsSwarmServiceAsync(Server server, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Deploying Traefik as HA swarm service with 2 replicas on manager nodes");

            // Ensure hostcraft-network overlay network exists (will be hostcraft_hostcraft-network in swarm)
            var networkId = await _dockerService.EnsureNetworkExistsAsync(server, "hostcraft_hostcraft-network", cancellationToken);
            _logger.LogInformation("Overlay network hostcraft_hostcraft-network ready: {NetworkId}", networkId);

            // Pull Traefik image first
            await _dockerService.PullImageAsync(server, "traefik:v2.11", null, null, cancellationToken);

            // Create service using low-level DockerService operations
            // We'll use a workaround: inspect a service to get a client handle, then use it
            // Actually, simpler approach: use CreateServiceAsync with all parameters we can, 
            // then update it with placement constraints
            
            // Build service request
            var serviceRequest = new CreateServiceRequest(
                Name: "traefik",
                Image: "traefik:v2.11",
                Replicas: 3,
                EnvironmentVariables: new Dictionary<string, string>(),
                Labels: new Dictionary<string, string>
                {
                    ["hostcraft.managed"] = "true",
                    ["hostcraft.proxy"] = "traefik",
                    ["hostcraft.service.type"] = "reverse-proxy"
                },
                Networks: new List<string> { "hostcraft_hostcraft-network" },
                PortMappings: new List<ServicePortMapping>
                {
                    new(HostPort: 80, ContainerPort: 80, Protocol: "tcp"),
                    new(HostPort: 443, ContainerPort: 443, Protocol: "tcp"),
                    new(HostPort: 8080, ContainerPort: 8080, Protocol: "tcp")
                },
                MemoryLimit: null,
                CpuLimit: null
            );

            // Create service via DockerService
            // Note: DockerService.CreateServiceAsync doesn't support Args, Mounts, or Constraints
            // We need to create a custom Traefik service helper
            var serviceId = await CreateTraefikSwarmServiceDirectAsync(server, networkId, cancellationToken);

            _logger.LogInformation("Traefik HA service deployed successfully (ID: {ServiceId})", serviceId);
            _logger.LogInformation("Dashboard accessible at http://{Host}:8080 from any manager node", server.Host);
            _logger.LogInformation("HTTP/HTTPS accessible at :80/:443 from any swarm node (ingress mode)");
            _logger.LogInformation("Service runs 2 replicas on manager nodes with automatic failover");
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deploy Traefik as swarm service");
            return false;
        }
    }

    /// <summary>
    /// Creates Traefik swarm service with full configuration including Args, Mounts, and Constraints.
    /// Direct implementation because IDockerService.CreateServiceAsync doesn't support all parameters.
    /// </summary>
    private async Task<string> CreateTraefikSwarmServiceDirectAsync(Server server, string networkId, CancellationToken cancellationToken)
    {
        // Create Docker client using SSH or local connection
        // Since GetClient is private in DockerService, we replicate the logic here
        DockerClient client;
        
        if (server.Host == "localhost" || server.Host == "127.0.0.1")
        {
            // Local Docker
            var uri = Environment.OSVersion.Platform == PlatformID.Win32NT
                ? "npipe://./pipe/docker_engine"
                : "unix:///var/run/docker.sock";
            client = new DockerClientConfiguration(new Uri(uri)).CreateClient();
        }
        else
        {
            // Remote Docker - for now, throw exception requiring SSH tunnel setup
            // In production, this would use SSH tunneling like DockerService does
            throw new NotSupportedException(
                "Traefik HA deployment to remote servers requires SSH tunneling. " +
                "This will be implemented in a future update. " +
                "For now, deploy Traefik manually or use standalone mode.");
        }

        // Validate Let's Encrypt email
        if (string.IsNullOrWhiteSpace(server.DefaultLetsEncryptEmail))
        {
            throw new InvalidOperationException(
                $"Cannot deploy Traefik: Server '{server.Name}' does not have a valid Let's Encrypt email. " +
                "Please set DefaultLetsEncryptEmail on the Server before deploying Traefik.");
        }

        // Build complete service specification
        var serviceSpec = new ServiceCreateParameters
        {
            Service = new ServiceSpec
            {
                Name = "traefik",
                TaskTemplate = new TaskSpec
                {
                    ContainerSpec = new ContainerSpec
                    {
                        Image = "traefik:v2.11",
                        Args = new List<string>
                        {
                            "--api=true",
                            "--api.dashboard=true",
                            "--api.insecure=true",
                            "--providers.swarm=true", // v3.6+ uses providers.swarm instead of docker.swarmMode
                            "--providers.swarm.exposedbydefault=false",
                            "--providers.swarm.network=hostcraft_hostcraft-network",
                            "--entrypoints.web.address=:80",
                            "--entrypoints.websecure.address=:443",
                            $"--certificatesresolvers.letsencrypt.acme.email={server.DefaultLetsEncryptEmail}",
                            "--certificatesresolvers.letsencrypt.acme.storage=/letsencrypt/acme.json",
                            "--certificatesresolvers.letsencrypt.acme.httpchallenge=true",
                            "--certificatesresolvers.letsencrypt.acme.httpchallenge.entrypoint=web"
                        },
                        Mounts = new List<Mount>
                        {
                            new Mount
                            {
                                Type = "bind",
                                Source = "/var/run/docker.sock",
                                Target = "/var/run/docker.sock",
                                ReadOnly = true
                            },
                            new Mount
                            {
                                Type = "volume",
                                Source = "traefik-letsencrypt",
                                Target = "/letsencrypt",
                                ReadOnly = false
                            }
                        },
                        Labels = new Dictionary<string, string>()
                    },
                    RestartPolicy = new SwarmRestartPolicy
                    {
                        Condition = "on-failure",
                        MaxAttempts = 3
                    },
                    Placement = new Placement
                    {
                        Constraints = new List<string> { "node.role==manager" }
                    },
                    Networks = new List<NetworkAttachmentConfig>
                    {
                        new NetworkAttachmentConfig { Target = networkId }
                    }
                },
                Mode = new ServiceMode
                {
                    Replicated = new ReplicatedService { Replicas = 2 }
                },
                UpdateConfig = new SwarmUpdateConfig
                {
                    Parallelism = 1,
                    Delay = 10_000_000_000,
                    FailureAction = "rollback"
                },
                EndpointSpec = new EndpointSpec
                {
                    Ports = new List<PortConfig>
                    {
                        new PortConfig
                        {
                            Protocol = "tcp",
                            TargetPort = 80,
                            PublishedPort = 80,
                            PublishMode = "ingress"
                        },
                        new PortConfig
                        {
                            Protocol = "tcp",
                            TargetPort = 443,
                            PublishedPort = 443,
                            PublishMode = "ingress"
                        },
                        new PortConfig
                        {
                            Protocol = "tcp",
                            TargetPort = 8080,
                            PublishedPort = 8080,
                            PublishMode = "host"
                        }
                    }
                },
                Labels = new Dictionary<string, string>
                {
                    ["hostcraft.managed"] = "true",
                    ["hostcraft.proxy"] = "traefik",
                    ["hostcraft.service.type"] = "reverse-proxy"
                }
            }
        };

        var response = await client.Swarm.CreateServiceAsync(serviceSpec, cancellationToken);
        return response.ID;
    }

    /// <summary>
    /// Deploys Traefik as a standalone container (non-HA mode).
    /// </summary>
    private async Task<bool> DeployTraefikAsContainerAsync(Server server, CancellationToken cancellationToken)
    {
        // Validate Let's Encrypt email
        if (string.IsNullOrWhiteSpace(server.DefaultLetsEncryptEmail))
        {
            throw new InvalidOperationException(
                $"Cannot deploy Traefik: Server '{server.Name}' does not have a valid Let's Encrypt email. " +
                "Please set DefaultLetsEncryptEmail on the Server before deploying Traefik.");
        }

        var containerName = GetProxyContainerName(ProxyType.Traefik);

        var createParams = new CreateContainerParameters
        {
            Name = containerName,
            Image = "traefik:v2.11",
            Cmd = new List<string>
            {
                "--api=true",
                "--api.dashboard=true",
                "--providers.docker=true",
                "--providers.docker.exposedbydefault=false",
                "--providers.docker.network=hostcraft-network",
                "--entrypoints.web.address=:80",
                "--entrypoints.websecure.address=:443",
                $"--certificatesresolvers.letsencrypt.acme.email={server.DefaultLetsEncryptEmail}",
                "--certificatesresolvers.letsencrypt.acme.storage=/letsencrypt/acme.json",
                "--certificatesresolvers.letsencrypt.acme.httpchallenge=true",
                "--certificatesresolvers.letsencrypt.acme.httpchallenge.entrypoint=web"
            },
            HostConfig = new HostConfig
            {
                Binds = new List<string>
                {
                    "/var/run/docker.sock:/var/run/docker.sock:ro",
                    "traefik-letsencrypt:/letsencrypt"
                },
                PortBindings = new Dictionary<string, IList<PortBinding>>
                {
                    ["80/tcp"] = new List<PortBinding> { new() { HostPort = "80" } },
                    ["443/tcp"] = new List<PortBinding> { new() { HostPort = "443" } },
                    ["8080/tcp"] = new List<PortBinding> { new() { HostPort = "8080" } }
                },
                RestartPolicy = new RestartPolicy { Name = RestartPolicyKind.UnlessStopped }
            },
            Labels = new Dictionary<string, string>
            {
                ["hostcraft.managed"] = "true",
                ["hostcraft.proxy"] = "traefik"
            },
            NetworkingConfig = new NetworkingConfig
            {
                EndpointsConfig = new Dictionary<string, EndpointSettings>
                {
                    ["hostcraft-network"] = new EndpointSettings()
                }
            }
        };

        try
        {
            // Ensure hostcraft-network exists (bridge driver for standalone)
            await _dockerService.EnsureNetworkExistsAsync(server, "hostcraft-network", cancellationToken);

            // Pull image first
            await _dockerService.PullImageAsync(server, "traefik:v2.11", null, null, cancellationToken);

            // Create and start container
            var containerId = await _dockerService.CreateContainerAsync(server, createParams, cancellationToken);
            await _dockerService.StartContainerAsync(server, containerId, cancellationToken);

            _logger.LogInformation("Traefik deployed successfully on {ServerName} (Dashboard: http://{Host}:8080)",
                server.Name, server.Host);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deploy Traefik");
            return false;
        }
    }

    private async Task<bool> DeployCaddyAsync(Server server, CancellationToken cancellationToken)
    {
        var containerName = GetProxyContainerName(ProxyType.Caddy);
        
        // Caddy will automatically configure HTTPS for all services via Docker labels
        var createParams = new CreateContainerParameters
        {
            Name = containerName,
            Image = "caddy:2.7-alpine",
            HostConfig = new HostConfig
            {
                Binds = new List<string>
                {
                    "/var/run/docker.sock:/var/run/docker.sock:ro",
                    "caddy-data:/data",
                    "caddy-config:/config"
                },
                PortBindings = new Dictionary<string, IList<PortBinding>>
                {
                    ["80/tcp"] = new List<PortBinding> { new() { HostPort = "80" } },
                    ["443/tcp"] = new List<PortBinding> { new() { HostPort = "443" } },
                    ["2019/tcp"] = new List<PortBinding> { new() { HostPort = "2019" } }
                },
                RestartPolicy = new RestartPolicy { Name = RestartPolicyKind.UnlessStopped }
            },
            Labels = new Dictionary<string, string>
            {
                ["hostcraft.managed"] = "true",
                ["hostcraft.proxy"] = "caddy"
            }
        };

        try
        {
            await _dockerService.PullImageAsync(server, "caddy:2.7-alpine", null, null, cancellationToken);
            var containerId = await _dockerService.CreateContainerAsync(server, createParams, cancellationToken);
            await _dockerService.StartContainerAsync(server, containerId, cancellationToken);
            
            _logger.LogInformation("Caddy deployed successfully on {ServerName} (Admin API: http://{Host}:2019)", 
                server.Name, server.Host);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deploy Caddy");
            return false;
        }
    }

    private async Task<bool> DeployNginxAsync(Server server, CancellationToken cancellationToken)
    {
        var containerName = GetProxyContainerName(ProxyType.Nginx);
        
        var createParams = new CreateContainerParameters
        {
            Name = containerName,
            Image = "nginx:alpine",
            HostConfig = new HostConfig
            {
                Binds = new List<string>
                {
                    "nginx-conf:/etc/nginx/conf.d",
                    "nginx-certs:/etc/nginx/certs",
                    "nginx-html:/usr/share/nginx/html"
                },
                PortBindings = new Dictionary<string, IList<PortBinding>>
                {
                    ["80/tcp"] = new List<PortBinding> { new() { HostPort = "80" } },
                    ["443/tcp"] = new List<PortBinding> { new() { HostPort = "443" } }
                },
                RestartPolicy = new RestartPolicy { Name = RestartPolicyKind.UnlessStopped }
            },
            Labels = new Dictionary<string, string>
            {
                ["hostcraft.managed"] = "true",
                ["hostcraft.proxy"] = "nginx"
            }
        };

        try
        {
            await _dockerService.PullImageAsync(server, "nginx:alpine", null, null, cancellationToken);
            var containerId = await _dockerService.CreateContainerAsync(server, createParams, cancellationToken);
            await _dockerService.StartContainerAsync(server, containerId, cancellationToken);
            
            _logger.LogInformation("Nginx deployed successfully on {ServerName} (HTTP: {HttpHost}:80, HTTPS: {HttpsHost}:443)", 
                server.Name, server.Host, server.Host);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deploy Nginx");
            return false;
        }
    }

    private async Task<bool> DeployYarpAsync(Server server, CancellationToken cancellationToken)
    {
        // YARP would be a custom ASP.NET Core application
        // For now, we'll note that this requires a custom deployment
        _logger.LogWarning("YARP deployment requires custom ASP.NET Core application - not yet fully implemented");
        return true;
    }

    private string GetProxyContainerName(ProxyType proxyType)
    {
        return proxyType switch
        {
            ProxyType.Traefik => "hostcraft-traefik",
            ProxyType.Caddy => "hostcraft-caddy",
            ProxyType.Nginx => "hostcraft-nginx",
            ProxyType.Yarp => "hostcraft-yarp",
            _ => "hostcraft-proxy"
        };
    }

    private string GenerateTraefikConfig(Application application)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Traefik Labels (automatically applied by HostCraft)");
        sb.AppendLine($"traefik.enable=true");
        sb.AppendLine($"traefik.http.routers.{application.Name}.rule=Host(`{application.Name}.yourdomain.com`)");
        sb.AppendLine($"traefik.http.routers.{application.Name}.entrypoints=websecure");
        sb.AppendLine($"traefik.http.routers.{application.Name}.tls.certresolver=letsencrypt");
        
        if (application.Port.HasValue)
        {
            sb.AppendLine($"traefik.http.services.{application.Name}.loadbalancer.server.port={application.Port}");
        }
        
        return sb.ToString();
    }

    private string GenerateCaddyConfig(Application application)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{application.Name}.yourdomain.com {{");
        sb.AppendLine($"    reverse_proxy {application.Name}:{application.Port ?? 80}");
        sb.AppendLine("    encode gzip");
        sb.AppendLine("    tls {");
        sb.AppendLine("        email admin@hostcraft.local");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private string GenerateNginxConfig(Application application)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Nginx Configuration for {application.Name}");
        sb.AppendLine($"server {{");
        sb.AppendLine($"    listen 80;");
        sb.AppendLine($"    server_name {application.Name}.yourdomain.com;");
        sb.AppendLine();
        sb.AppendLine($"    location / {{");
        sb.AppendLine($"        proxy_pass http://{application.Name}:{application.Port ?? 80};");
        sb.AppendLine($"        proxy_set_header Host $host;");
        sb.AppendLine($"        proxy_set_header X-Real-IP $remote_addr;");
        sb.AppendLine($"        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;");
        sb.AppendLine($"        proxy_set_header X-Forwarded-Proto $scheme;");
        sb.AppendLine($"    }}");
        sb.AppendLine($"}}");
        sb.AppendLine();
        sb.AppendLine($"# For HTTPS, use certbot: certbot --nginx -d {application.Name}.yourdomain.com");
        return sb.ToString();
    }

    private string GenerateYarpConfig(Application application)
    {
        return $@"{{
  ""ReverseProxy"": {{
    ""Routes"": {{
      ""{application.Name}-route"": {{
        ""ClusterId"": ""{application.Name}-cluster"",
        ""Match"": {{
          ""Hosts"": [""{application.Name}.yourdomain.com""]
        }}
      }}
    }},
    ""Clusters"": {{
      ""{application.Name}-cluster"": {{
        ""Destinations"": {{
          ""destination1"": {{
            ""Address"": ""http://{application.Name}:{application.Port ?? 80}""
          }}
        }}
      }}
    }}
  }}
}}";
    }

    public async Task<bool> ConfigureHostCraftDomainAsync(
        string domain,
        bool enableHttps,
        string? letsEncryptEmail,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Configuring HostCraft domain: {Domain} (HTTPS: {EnableHttps}, Email: {Email})",
                domain, enableHttps, letsEncryptEmail ?? "None");

            // Update Traefik service with new Let's Encrypt email if HTTPS is enabled
            if (enableHttps && !string.IsNullOrWhiteSpace(letsEncryptEmail))
            {
                _logger.LogInformation("Updating Traefik service with Let's Encrypt email: {Email}", letsEncryptEmail);
                await UpdateTraefikEmailAsync(letsEncryptEmail, cancellationToken);
            }

            // Generate Traefik labels for the HostCraft web service
            var labels = new Dictionary<string, string>
            {
                // Enable Traefik
                ["traefik.enable"] = "true",

                // HTTP router
                ["traefik.http.routers.hostcraft-web.rule"] = $"Host(`{domain}`)",
                ["traefik.http.routers.hostcraft-web.entrypoints"] = enableHttps ? "websecure" : "web",
                ["traefik.http.routers.hostcraft-web.service"] = "hostcraft-web",

                // Service configuration
                ["traefik.http.services.hostcraft-web.loadbalancer.server.port"] = "8080",

            };

            // HTTP router (always enabled for both HTTP access and ACME challenges)
            labels["traefik.http.routers.hostcraft-web-http.rule"] = $"Host(`{domain}`)";
            labels["traefik.http.routers.hostcraft-web-http.entrypoints"] = "web";
            labels["traefik.http.routers.hostcraft-web-http.service"] = "hostcraft-web";
            labels["traefik.http.routers.hostcraft-web-http.priority"] = "1";

            if (enableHttps)
            {
                // HTTPS configuration with Let's Encrypt
                labels["traefik.http.routers.hostcraft-web.tls"] = "true";
                labels["traefik.http.routers.hostcraft-web.tls.certresolver"] = "letsencrypt";

                // Add HTTPS redirect middleware to HTTP router
                labels["traefik.http.routers.hostcraft-web-http.middlewares"] = "redirect-to-https";
                labels["traefik.http.middlewares.redirect-to-https.redirectscheme.scheme"] = "https";
                labels["traefik.http.middlewares.redirect-to-https.redirectscheme.permanent"] = "true";
            }

            _logger.LogInformation("Applying Traefik labels to hostcraft-web service...");

            // Apply labels to the hostcraft-web service
            // This will automatically restart the service with new configuration
            await ApplyLabelsToHostCraftWebService(labels, cancellationToken);

            _logger.LogInformation("HostCraft domain configuration completed for {Domain}. Service will be accessible shortly.", domain);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to configure HostCraft domain {Domain}", domain);
            return false;
        }
    }

    public async Task<bool> ConfigureTraefikDashboardAsync(
        string? dashboardDomain,
        bool enableAuth,
        string? username,
        string? passwordHash,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Configuring Traefik dashboard - Domain: {Domain}, Auth: {AuthEnabled}",
                dashboardDomain ?? "None", enableAuth);

            // Traefik file provider directory (must match what's mounted in docker-compose.yml)
            const string traefikDynamicPath = "/var/lib/hostcraft/traefik/dynamic";
            const string configFileName = "hostcraft-dashboard.yml";
            var configFilePath = Path.Combine(traefikDynamicPath, configFileName);

            // Ensure the directory exists
            Directory.CreateDirectory(traefikDynamicPath);

            if (!string.IsNullOrEmpty(dashboardDomain))
            {
                _logger.LogInformation("Creating Traefik dynamic config file at: {ConfigPath}", configFilePath);

                // Build the YAML configuration
                var config = new StringBuilder();
                config.AppendLine("# HostCraft Traefik Dashboard Configuration");
                config.AppendLine("# Auto-generated - Do not edit manually");
                config.AppendLine();
                config.AppendLine("http:");
                config.AppendLine("  routers:");
                config.AppendLine("    traefik-dashboard-https:");
                config.AppendLine($"      rule: \"Host(`{dashboardDomain}`)\"");
                config.AppendLine("      entryPoints:");
                config.AppendLine("        - websecure");
                config.AppendLine("      service: api@internal");
                config.AppendLine("      tls:");
                config.AppendLine("        certResolver: letsencrypt");

                // Add authentication middleware if enabled
                if (enableAuth && !string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(passwordHash))
                {
                    config.AppendLine("      middlewares:");
                    config.AppendLine("        - traefik-dashboard-auth");
                }

                // ACME HTTP-01 Challenge Router (CRITICAL for Let's Encrypt)
                // Higher priority to handle challenges before redirect
                config.AppendLine("    traefik-dashboard-acme:");
                config.AppendLine($"      rule: \"Host(`{dashboardDomain}`) && PathPrefix(`/.well-known/acme-challenge/`)\"");
                config.AppendLine("      entryPoints:");
                config.AppendLine("        - web");
                config.AppendLine("      priority: 100");
                config.AppendLine("      service: api@internal");

                // HTTP to HTTPS redirect router (lower priority for all other traffic)
                config.AppendLine("    traefik-dashboard-http:");
                config.AppendLine($"      rule: \"Host(`{dashboardDomain}`)\"");
                config.AppendLine("      entryPoints:");
                config.AppendLine("        - web");
                config.AppendLine("      priority: 1");
                config.AppendLine("      service: api@internal");
                config.AppendLine("      middlewares:");
                config.AppendLine("        - redirect-to-https");

                // Middlewares section
                config.AppendLine();
                config.AppendLine("  middlewares:");
                
                // HTTPS redirect middleware (always needed)
                config.AppendLine("    redirect-to-https:");
                config.AppendLine("      redirectScheme:");
                config.AppendLine("        scheme: https");
                config.AppendLine("        permanent: true");

                // Basic auth middleware (if enabled)
                if (enableAuth && !string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(passwordHash))
                {
                    // Remove any double-dollar escaping (not needed in YAML files)
                    var cleanPasswordHash = passwordHash.Replace("$$", "$");
                    config.AppendLine("    traefik-dashboard-auth:");
                    config.AppendLine("      basicAuth:");
                    config.AppendLine("        users:");
                    config.AppendLine($"          - \"{username}:{cleanPasswordHash}\"");
                }

                // Write the configuration file
                await File.WriteAllTextAsync(configFilePath, config.ToString(), cancellationToken);
                
                _logger.LogInformation("Traefik dashboard config file created. Dashboard will be accessible at: https://{DashboardDomain}", dashboardDomain);
            }
            else
            {
                // No domain configured - remove the config file
                if (File.Exists(configFilePath))
                {
                    File.Delete(configFilePath);
                    _logger.LogInformation("Traefik dashboard config file removed. Dashboard accessible via port 8080 only.");
                }
                else
                {
                    _logger.LogInformation("No dashboard domain configured and no config file exists.");
                }
            }

            _logger.LogInformation("Traefik dashboard configuration completed successfully. Traefik will reload config automatically.");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to configure Traefik dashboard");
            return false;
        }
    }

    private async Task ApplyLabelsToHostCraftWebService(
        Dictionary<string, string> labels,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Applying {LabelCount} labels to hostcraft-web service/container using Docker API", labels.Count);

            // Connect to Docker socket directly (works inside container with mounted socket)
            using var dockerClient = new DockerClientConfiguration(
                new Uri("unix:///var/run/docker.sock")).CreateClient();

            // Try Swarm mode first
            try
            {
                var services = await dockerClient.Swarm.ListServicesAsync(cancellationToken: cancellationToken);
                var webService = services.FirstOrDefault(s =>
                    s.Spec.Name.Contains("hostcraft-web", StringComparison.OrdinalIgnoreCase) ||
                    s.Spec.Name.Contains("hostcraft_web", StringComparison.OrdinalIgnoreCase) ||
                    s.Spec.Name.Contains("hostcraft_hostcraft-web", StringComparison.OrdinalIgnoreCase));

                if (webService != null)
                {
                    _logger.LogInformation("Found Swarm service: {ServiceName} (ID: {ServiceId})", webService.Spec.Name, webService.ID);

                    // Get current service spec
                    var currentSpec = webService.Spec;

                    // Ensure service is connected to traefik-public network
                    var serviceNetworks = currentSpec.TaskTemplate.Networks ?? new List<NetworkAttachmentConfig>();
                    var hostcraftNetworkAttached = serviceNetworks.Any(n =>
                    {
                        try
                        {
                            // Get network details to check name
                            var networkTask = dockerClient.Networks.InspectNetworkAsync(n.Target, cancellationToken);
                            networkTask.Wait(cancellationToken);
                            var networkName = networkTask.Result?.Name;
                            return networkName == "hostcraft_hostcraft-network" || networkName == "hostcraft-network";
                        }
                        catch
                        {
                            return false;
                        }
                    });

                    if (!hostcraftNetworkAttached)
                    {
                        _logger.LogInformation("Service not on hostcraft-network, adding it...");

                        // Get hostcraft network ID (may be hostcraft_hostcraft-network in swarm)
                        var allNetworks = await dockerClient.Networks.ListNetworksAsync(cancellationToken: cancellationToken);
                        var hostcraftNetwork = allNetworks.FirstOrDefault(n =>
                            n.Name == "hostcraft_hostcraft-network" || n.Name == "hostcraft-network");

                        if (hostcraftNetwork == null)
                        {
                            _logger.LogWarning("hostcraft-network not found. Creating it...");
                            var networkCreateResponse = await dockerClient.Networks.CreateNetworkAsync(new NetworksCreateParameters
                            {
                                Name = "hostcraft-network",
                                Driver = "overlay",
                                Attachable = true
                            }, cancellationToken);
                            hostcraftNetwork = new NetworkResponse { ID = networkCreateResponse.ID, Name = "hostcraft-network" };
                        }

                        // Add hostcraft-network to the service networks
                        var networksList = serviceNetworks.ToList();
                        networksList.Add(new NetworkAttachmentConfig { Target = hostcraftNetwork.ID });
                        currentSpec.TaskTemplate.Networks = networksList;

                        _logger.LogInformation("Added hostcraft-network (ID: {NetworkId})", hostcraftNetwork.ID);
                    }

                    // Update labels
                    currentSpec.Labels ??= new Dictionary<string, string>();
                    foreach (var label in labels)
                    {
                        currentSpec.Labels[label.Key] = label.Value;
                    }

                    // Update the service with new labels and networks
                    var updateParams = new ServiceUpdateParameters
                    {
                        Service = currentSpec,
                        Version = (long)webService.Version.Index
                    };

                    await dockerClient.Swarm.UpdateServiceAsync(webService.ID, updateParams, cancellationToken);
                    _logger.LogInformation("Successfully updated Swarm service {ServiceName} with labels and traefik-public network", webService.Spec.Name);
                    return;
                }
            }
            catch (Exception swarmEx)
            {
                _logger.LogDebug(swarmEx, "Not running in Swarm mode, trying standalone containers");
            }

            // Try standalone containers
            var containers = await dockerClient.Containers.ListContainersAsync(
                new ContainersListParameters { All = true },
                cancellationToken);
            
            var webContainer = containers.FirstOrDefault(c =>
                c.Names.Any(n => n.Contains("hostcraft-web", StringComparison.OrdinalIgnoreCase) ||
                               n.Contains("hostcraft_web", StringComparison.OrdinalIgnoreCase)));

            if (webContainer == null)
            {
                _logger.LogWarning("Could not find hostcraft-web container or service. Available containers: {Containers}",
                    string.Join(", ", containers.SelectMany(c => c.Names)));
                return;
            }

            _logger.LogInformation("Found container: {ContainerName} (ID: {ContainerId})", 
                string.Join(", ", webContainer.Names), webContainer.ID);

            // Get container details
            var containerInspect = await dockerClient.Containers.InspectContainerAsync(webContainer.ID, cancellationToken);
            
            // Merge labels
            var mergedLabels = new Dictionary<string, string>(containerInspect.Config.Labels ?? new Dictionary<string, string>());
            foreach (var label in labels)
            {
                mergedLabels[label.Key] = label.Value;
            }

            // Ensure hostcraft-network exists
            var existingNetworks = await dockerClient.Networks.ListNetworksAsync(cancellationToken: cancellationToken);
            var containerHostcraftNetwork = existingNetworks.FirstOrDefault(n =>
                n.Name == "hostcraft_hostcraft-network" || n.Name == "hostcraft-network");

            if (containerHostcraftNetwork == null)
            {
                var networkCreateResponse = await dockerClient.Networks.CreateNetworkAsync(new NetworksCreateParameters
                {
                    Name = "hostcraft-network",
                    Driver = "overlay",
                    Attachable = true
                }, cancellationToken);
                containerHostcraftNetwork = new NetworkResponse { ID = networkCreateResponse.ID, Name = "hostcraft-network" };
            }

            // Connect container to hostcraft-network if not already connected
            var networks = containerInspect.NetworkSettings.Networks;
            var networkName = containerHostcraftNetwork.Name;
            if (!networks.ContainsKey(networkName))
            {
                _logger.LogInformation("Connecting container to {NetworkName} network...", networkName);
                await dockerClient.Networks.ConnectNetworkAsync(networkName,
                    new NetworkConnectParameters { Container = webContainer.ID },
                    cancellationToken);
            }

            // Update container labels by recreating it
            _logger.LogInformation("Recreating container with updated labels...");
            
            // Stop container
            await dockerClient.Containers.StopContainerAsync(webContainer.ID, 
                new ContainerStopParameters { WaitBeforeKillSeconds = 10 }, 
                cancellationToken);

            // Remove container
            await dockerClient.Containers.RemoveContainerAsync(webContainer.ID, 
                new ContainerRemoveParameters { Force = true }, 
                cancellationToken);

            // Recreate with new labels
            var createParams = new CreateContainerParameters
            {
                Name = webContainer.Names.FirstOrDefault()?.TrimStart('/'),
                Image = containerInspect.Config.Image,
                Cmd = containerInspect.Config.Cmd,
                Env = containerInspect.Config.Env,
                HostConfig = containerInspect.HostConfig,
                Labels = mergedLabels,
                NetworkingConfig = new NetworkingConfig
                {
                    EndpointsConfig = new Dictionary<string, EndpointSettings>
                    {
                        [networkName] = new EndpointSettings()
                    }
                }
            };

            var newContainer = await dockerClient.Containers.CreateContainerAsync(createParams, cancellationToken);
            await dockerClient.Containers.StartContainerAsync(newContainer.ID, new ContainerStartParameters(), cancellationToken);

            _logger.LogInformation("Successfully recreated hostcraft-web container with {LabelCount} labels", labels.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply labels to hostcraft-web service/container");
            // Don't throw - this is a non-critical operation
        }
    }

    /// <summary>
    /// Checks if Traefik service has Let's Encrypt ACME configuration.
    /// </summary>
    private async Task<bool> CheckTraefikHasLetsEncryptAsync(Server server, CancellationToken cancellationToken)
    {
        try
        {
            var services = await _dockerService.ListServicesAsync(server, cancellationToken);
            var traefikService = services.FirstOrDefault(s =>
                s.Name.Contains("traefik", StringComparison.OrdinalIgnoreCase));

            if (traefikService == null)
                return false;

            // Get Docker client and inspect service directly to access Args
            var client = _dockerService.GetDockerClient(server);
            var serviceDetails = await client.Swarm.InspectServiceAsync(traefikService.Id, cancellationToken);

            if (serviceDetails?.Spec?.TaskTemplate?.ContainerSpec?.Args == null)
                return false;

            var args = serviceDetails.Spec.TaskTemplate.ContainerSpec.Args;

            // Check if service has ACME configuration
            var hasAcmeEmail = args.Any(arg => arg.Contains("certificatesresolvers.letsencrypt.acme.email"));
            var hasAcmeStorage = args.Any(arg => arg.Contains("certificatesresolvers.letsencrypt.acme.storage"));

            _logger.LogInformation("Traefik Let's Encrypt check: Email={HasEmail}, Storage={HasStorage}",
                hasAcmeEmail, hasAcmeStorage);

            return hasAcmeEmail && hasAcmeStorage;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check Traefik Let's Encrypt configuration");
            return false;
        }
    }

    /// <summary>
    /// Removes Traefik service from Docker Swarm.
    /// </summary>
    private async Task RemoveTraefikServiceAsync(Server server, CancellationToken cancellationToken)
    {
        try
        {
            var services = await _dockerService.ListServicesAsync(server, cancellationToken);
            var traefikService = services.FirstOrDefault(s =>
                s.Name.Contains("traefik", StringComparison.OrdinalIgnoreCase));

            if (traefikService != null)
            {
                _logger.LogInformation("Removing Traefik service {ServiceName} for redeployment", traefikService.Name);
                await _dockerService.RemoveServiceAsync(server, traefikService.Id, cancellationToken);

                // Wait for service to fully stop
                await Task.Delay(5000, cancellationToken);

                _logger.LogInformation("Traefik service removed successfully");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove Traefik service");
            throw;
        }
    }

    /// <summary>
    /// Updates the Traefik service with a new Let's Encrypt email address.
    /// This updates the command-line arguments that Traefik uses for certificate requests.
    /// </summary>
    private async Task UpdateTraefikEmailAsync(string email, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Updating Traefik Let's Encrypt email to: {Email}", email);

            // Connect to local Docker socket
            using var dockerClient = new DockerClientConfiguration(
                new Uri("unix:///var/run/docker.sock")).CreateClient();

            // Find Traefik service
            var services = await dockerClient.Swarm.ListServicesAsync(cancellationToken: cancellationToken);
            var traefikService = services.FirstOrDefault(s =>
                s.Spec.Name.Contains("traefik", StringComparison.OrdinalIgnoreCase) ||
                s.Spec.Name.Contains("hostcraft_traefik", StringComparison.OrdinalIgnoreCase));

            if (traefikService == null)
            {
                _logger.LogWarning("Traefik service not found. Cannot update Let's Encrypt email.");
                return;
            }

            _logger.LogInformation("Found Traefik service: {ServiceName} (ID: {ServiceId})",
                traefikService.Spec.Name, traefikService.ID);

            // Get current spec
            var currentSpec = traefikService.Spec;

            // Update command-line arguments (Args) - Traefik reads email from Args, not environment variables
            var args = currentSpec.TaskTemplate.ContainerSpec.Args?.ToList() ?? new List<string>();

            // Remove old ACME email argument if exists
            args.RemoveAll(a => a.StartsWith("--certificatesresolvers.letsencrypt.acme.email="));

            // Add new email argument
            args.Add($"--certificatesresolvers.letsencrypt.acme.email={email}");

            currentSpec.TaskTemplate.ContainerSpec.Args = args;

            // Update the service
            var updateParams = new ServiceUpdateParameters
            {
                Service = currentSpec,
                Version = (long)traefikService.Version.Index
            };

            await dockerClient.Swarm.UpdateServiceAsync(traefikService.ID, updateParams, cancellationToken);

            _logger.LogInformation("Traefik service updated with new Let's Encrypt email. Service will restart automatically.");
            _logger.LogInformation("Certificate issuance may take 1-2 minutes after the service restarts.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update Traefik service with new email");
            // Don't throw - this is important but shouldn't break the domain configuration
        }
    }
}
