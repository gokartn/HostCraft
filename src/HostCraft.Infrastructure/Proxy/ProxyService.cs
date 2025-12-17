using System.Text;
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
                application.Server.ProxyType, application.Name);

            // Ensure proxy is deployed on the server
            await EnsureProxyDeployedAsync(application.Server, cancellationToken);

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
                application.Server.ProxyType, application.Name);

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
        if (application.Server?.ProxyType == ProxyType.None)
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

        var proxyName = GetProxyContainerName(server.ProxyType);
        
        try
        {
            // Check if proxy container already exists
            var containers = await _dockerService.ListContainersAsync(server, true, cancellationToken);
            var existingProxy = containers.FirstOrDefault(c => 
                c.Name.Contains(proxyName, StringComparison.OrdinalIgnoreCase));

            if (existingProxy != null)
            {
                _logger.LogInformation("{ProxyType} already deployed on {ServerName}", 
                    server.ProxyType, server.Name);
                return true;
            }

            _logger.LogInformation("Deploying {ProxyType} on server {ServerName}", 
                server.ProxyType, server.Name);

            return server.ProxyType switch
            {
                ProxyType.Traefik => await DeployTraefikAsync(server, cancellationToken),
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

    private async Task<bool> DeployTraefikAsync(Server server, CancellationToken cancellationToken)
    {
        var containerName = GetProxyContainerName(ProxyType.Traefik);
        
        var createParams = new CreateContainerParameters
        {
            Name = containerName,
            Image = "traefik:v2.11",
            Cmd = new List<string>
            {
                "--api.insecure=true",
                "--providers.docker=true",
                "--providers.docker.exposedbydefault=false",
                "--entrypoints.web.address=:80",
                "--entrypoints.websecure.address=:443",
                "--certificatesresolvers.letsencrypt.acme.httpchallenge=true",
                "--certificatesresolvers.letsencrypt.acme.httpchallenge.entrypoint=web",
                $"--certificatesresolvers.letsencrypt.acme.email={server.DefaultLetsEncryptEmail ?? "admin@hostcraft.local"}",
                "--certificatesresolvers.letsencrypt.acme.storage=/letsencrypt/acme.json",
                "--certificatesresolvers.letsencrypt.acme.caserver=https://acme-v02.api.letsencrypt.org/directory"
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
            }
        };

        try
        {
            // Pull image first
            await _dockerService.PullImageAsync(server, "traefik:v2.11", null, cancellationToken);
            
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
            await _dockerService.PullImageAsync(server, "caddy:2.7-alpine", null, cancellationToken);
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
            await _dockerService.PullImageAsync(server, "nginx:alpine", null, cancellationToken);
            var containerId = await _dockerService.CreateContainerAsync(server, createParams, cancellationToken);
            await _dockerService.StartContainerAsync(server, containerId, cancellationToken);
            
            _logger.LogInformation("Nginx deployed successfully on {ServerName} (HTTP: {Host}:80, HTTPS: {Host}:443)", 
                server.Name, server.Host);
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
            _logger.LogInformation("Configuring HostCraft domain: {Domain} (HTTPS: {EnableHttps})", domain, enableHttps);

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
                
                // Docker Swarm specific
                ["traefik.docker.network"] = "traefik-public"
            };

            if (enableHttps)
            {
                // HTTPS configuration with Let's Encrypt
                labels["traefik.http.routers.hostcraft-web.tls"] = "true";
                labels["traefik.http.routers.hostcraft-web.tls.certresolver"] = "letsencrypt";
                
                // HTTP to HTTPS redirect
                labels["traefik.http.routers.hostcraft-web-http.rule"] = $"Host(`{domain}`)";
                labels["traefik.http.routers.hostcraft-web-http.entrypoints"] = "web";
                labels["traefik.http.routers.hostcraft-web-http.middlewares"] = "redirect-to-https";
                labels["traefik.http.middlewares.redirect-to-https.redirectscheme.scheme"] = "https";
                labels["traefik.http.middlewares.redirect-to-https.redirectscheme.permanent"] = "true";
            }

            // Log instructions for manual update
            // In the future, we can implement automatic service update via Docker API
            _logger.LogInformation("HostCraft domain configuration completed for {Domain}. To apply changes, run: docker service update --force hostcraft_hostcraft-web", domain);
            _logger.LogInformation("Required labels:");
            foreach (var label in labels)
            {
                _logger.LogInformation("  {Key}={Value}", label.Key, label.Value);
            }
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to configure HostCraft domain {Domain}", domain);
            return false;
        }
    }
}
