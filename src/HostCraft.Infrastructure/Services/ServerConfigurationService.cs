using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using HostCraft.Core.Interfaces;
using HostCraft.Core.Interfaces.Repositories;
using System.Runtime.InteropServices;
using Docker.DotNet;
using System.Linq;

namespace HostCraft.Infrastructure.Services;

public class ServerConfigurationService : IServerConfigurationService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ServerConfigurationService> _logger;
    
    public ServerConfigurationService(
        IServiceScopeFactory scopeFactory,
        ILogger<ServerConfigurationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<ServerConfigurationResult> StartAutoConfigureAsync(int serverId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var serverRepository = scope.ServiceProvider.GetRequiredService<IServerRepository>();
        var server = await serverRepository.GetByIdWithPrivateKeyAsync(serverId, cancellationToken);

        if (server == null)
        {
            return new ServerConfigurationResult(false, $"Server {serverId} not found", NotFound: true);
        }

        if (server.Host == "localhost" || server.Host == "127.0.0.1")
        {
            return new ServerConfigurationResult(false, "Cannot auto-configure localhost. Docker should be installed locally.");
        }

        _logger.LogInformation("Starting auto-configuration for server {ServerName} ({Host})", server.Name, server.Host);

        _ = Task.Run(async () => await AutoConfigureServerAsync(serverId), cancellationToken);

        return new ServerConfigurationResult(true, "Auto-configuration started. This may take several minutes. Check server status for progress.");
    }
    
    public async Task AutoConfigureServerAsync(int serverId)
    {
        using var scope = _scopeFactory.CreateScope();
        var serverRepository = scope.ServiceProvider.GetRequiredService<IServerRepository>();
        var sshService = scope.ServiceProvider.GetRequiredService<ISshService>();
        var dockerService = scope.ServiceProvider.GetRequiredService<IDockerService>();
        
        var server = await serverRepository.GetByIdWithPrivateKeyAsync(serverId);
        
        if (server == null)
        {
            _logger.LogWarning("Server {ServerId} not found during auto-configuration", serverId);
            return;
        }
        
        try
        {
            _logger.LogInformation("Auto-configuring server {ServerName}...", server.Name);
            
            // Read the install-docker.sh script
            var scriptPath = Path.Combine(AppContext.BaseDirectory, "scripts", "install-docker.sh");
            if (!File.Exists(scriptPath))
            {
                // Try relative path if running in development
                scriptPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "scripts", "install-docker.sh");
                if (!File.Exists(scriptPath))
                {
                    _logger.LogError("install-docker.sh script not found at {Path}", scriptPath);
                    return;
                }
            }
            
            var installScript = await File.ReadAllTextAsync(scriptPath);
            
            // Upload and execute installation script
            if (!await InstallDockerAsync(server, installScript, sshService))
            {
                server.Status = ServerStatus.Error;
                await serverRepository.UpdateAsync(server);
                return;
            }
            
            // Validate Docker installation
            if (!await ValidateDockerInstallationAsync(server, sshService, dockerService, serverRepository))
            {
                _logger.LogWarning("Docker installed but validation failed for {ServerName}", server.Name);
                server.Status = ServerStatus.Offline;
                await serverRepository.UpdateAsync(server);
                return;
            }
            
            // Configure Swarm based on server type
            await ConfigureSwarmAsync(server, sshService, dockerService, serverRepository);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during auto-configuration of server {ServerId}", serverId);
            var serverToUpdate = await serverRepository.GetByIdAsync(serverId);
            if (serverToUpdate != null)
            {
                serverToUpdate.Status = ServerStatus.Error;
                await serverRepository.UpdateAsync(serverToUpdate);
            }
        }
    }
    
    private async Task<bool> InstallDockerAsync(Server server, string installScript, ISshService sshService)
    {
        try
        {
            _logger.LogInformation("Uploading installation script to {ServerName}...", server.Name);
            var remoteScriptPath = "/tmp/install-docker-hostcraft.sh";
            
            // Create script on remote server using cat with heredoc
            var uploadCommand = $"cat > {remoteScriptPath} << 'HOSTCRAFT_EOF'\n{installScript}\nHOSTCRAFT_EOF\nchmod +x {remoteScriptPath}";
            var uploadResult = await sshService.ExecuteCommandAsync(server, uploadCommand);
            
            if (uploadResult.ExitCode != 0)
            {
                _logger.LogError("Failed to upload script: {Error}", uploadResult.Error);
                return false;
            }
            
            _logger.LogInformation("Running Docker installation on {ServerName}...", server.Name);
            
            // Execute the script with sudo (non-interactive mode)
            var installCommand = $"sudo DEBIAN_FRONTEND=noninteractive bash {remoteScriptPath} 2>&1";
            var installResult = await sshService.ExecuteCommandAsync(server, installCommand);
            
            _logger.LogInformation("Installation output: {Output}", installResult.Output);
            
            if (installResult.ExitCode == 0)
            {
                _logger.LogInformation("Docker installed successfully on {ServerName}", server.Name);
                
                // Clean up the script
                try
                {
                    await sshService.ExecuteCommandAsync(server, $"rm -f {remoteScriptPath}");
                }
                catch
                {
                    // Ignore cleanup errors
                }
                
                return true;
            }
            else
            {
                _logger.LogError("Docker installation failed on {ServerName}: {Error}", server.Name, installResult.Error);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error installing Docker on {ServerName}", server.Name);
            return false;
        }
    }
    
    private async Task<bool> ValidateDockerInstallationAsync(
        Server server,
        ISshService sshService,
        IDockerService dockerService,
        IServerRepository serverRepository)
    {
        try
        {
            // Wait for Docker to fully initialize
            _logger.LogInformation("Waiting for Docker daemon to be ready...");
            await Task.Delay(10000);
            
            // Try to reconnect and validate (server might have restarted)
            var maxRetries = 5;
            var retryDelay = 5000;
            
            for (int retry = 0; retry < maxRetries; retry++)
            {
                try
                {
                    _logger.LogInformation("Validating Docker installation (attempt {Attempt}/{Max})...", retry + 1, maxRetries);
                    
                    // Test SSH connection first
                    var sshConnected = await sshService.ValidateConnectionAsync(server);
                    if (!sshConnected)
                    {
                        _logger.LogWarning("SSH connection lost, server may have restarted. Waiting...");
                        await Task.Delay(retryDelay);
                        continue;
                    }
                    
                    // Test Docker connection
                    var isValid = await dockerService.ValidateConnectionAsync(server);
                    if (isValid)
                    {
                        server.Status = ServerStatus.Online;
                        await serverRepository.UpdateAsync(server);
                        _logger.LogInformation("✅ Docker successfully validated on {ServerName}", server.Name);
                        return true;
                    }
                    else
                    {
                        _logger.LogWarning("Docker not ready yet, retrying...");
                        await Task.Delay(retryDelay);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Validation attempt {Attempt} failed, will retry", retry + 1);
                    if (retry < maxRetries - 1)
                    {
                        await Task.Delay(retryDelay);
                    }
                }
            }
            
            // Final validation
            var finalValidation = await dockerService.ValidateConnectionAsync(server);
            server.Status = finalValidation ? ServerStatus.Online : ServerStatus.Offline;
            await serverRepository.UpdateAsync(server);
            
            return finalValidation;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating Docker installation on {ServerName}", server.Name);
            return false;
        }
    }
    
    private async Task ConfigureSwarmAsync(
        Server server,
        ISshService sshService,
        IDockerService dockerService,
        IServerRepository serverRepository)
    {
        if (server.Status != ServerStatus.Online)
            return;
            
        if (server.Type == ServerType.SwarmManager)
        {
            await ConfigureSwarmManagerAsync(server, sshService, dockerService, serverRepository);
        }
        else if (server.Type == ServerType.SwarmWorker)
        {
            await ConfigureSwarmWorkerAsync(server, sshService, dockerService, serverRepository);
        }
        else
        {
            _logger.LogInformation("Server {ServerName} marked as Standalone, Docker installed without swarm configuration", server.Name);
        }
    }
    
    private async Task ConfigureSwarmManagerAsync(
        Server server,
        ISshService sshService,
        IDockerService dockerService,
        IServerRepository serverRepository)
    {
        _logger.LogInformation("Server {ServerName} marked as SwarmManager, checking swarm status after auto-configure", server.Name);
        
        try
        {
            // Check if already part of a swarm
            var systemInfo = await dockerService.GetSystemInfoAsync(server);
            
            if (systemInfo?.SwarmActive == true)
            {
                _logger.LogInformation("{ServerName} is already part of an active swarm", server.Name);
                return;
            }
            
            // Initialize swarm on this manager
            _logger.LogInformation("Initializing Docker Swarm on {ServerName}", server.Name);
            
            // Determine the advertise address
            string advertiseAddr;
            if (server.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || 
                server.Host == "127.0.0.1" || 
                server.Host == "::1")
            {
                // For localhost, detect the external IPv4 address only
                advertiseAddr = "$(hostname -I | tr ' ' '\\n' | grep -E '^[0-9]+\\.[0-9]+\\.[0-9]+\\.[0-9]+$' | grep -v '^127\\.' | head -n1)";
            }
            else
            {
                // Use the configured Host as advertise address
                advertiseAddr = server.Host;
            }
            
            var initCommand = $"docker swarm init --advertise-addr {advertiseAddr}";
            var result = await sshService.ExecuteCommandAsync(server, initCommand);
            
            if (result.ExitCode == 0 || result.Output.Contains("Swarm initialized"))
            {
                _logger.LogInformation("Successfully initialized swarm on {ServerName}", server.Name);
                server.IsSwarmManager = true;
                
                // Store the manager address for workers to join
                if (advertiseAddr.StartsWith("$("))
                {
                    // Extract the actual IPv4 address
                    var ipCommand = "hostname -I | tr ' ' '\\n' | grep -E '^[0-9]+\\.[0-9]+\\.[0-9]+\\.[0-9]+$' | grep -v '^127\\.' | head -n1";
                    var ipResult = await sshService.ExecuteCommandAsync(server, ipCommand);
                    if (ipResult.ExitCode == 0 && !string.IsNullOrWhiteSpace(ipResult.Output))
                    {
                        server.SwarmManagerAddress = $"{ipResult.Output.Trim()}:2377";
                    }
                }
                else
                {
                    server.SwarmManagerAddress = $"{advertiseAddr}:2377";
                }
                
                await serverRepository.UpdateAsync(server);
            }
            else
            {
                _logger.LogError("Failed to initialize swarm on {ServerName}: {Output}", server.Name, result.Output + result.Error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing swarm on {ServerName} after auto-configure", server.Name);
        }
    }
    
    private async Task ConfigureSwarmWorkerAsync(
        Server server,
        ISshService sshService,
        IDockerService dockerService,
        IServerRepository serverRepository)
    {
        _logger.LogInformation("Server {ServerName} marked as SwarmWorker, attempting to join swarm after auto-configure", server.Name);
        
        try
        {
            // Find an active swarm manager
            var managers = await serverRepository.GetSwarmManagersAsync();
            var swarmManager = managers.FirstOrDefault(s => s.Status == ServerStatus.Online);
            
            if (swarmManager == null)
            {
                _logger.LogWarning("No active swarm manager found to join {ServerName} to swarm", server.Name);
                return;
            }
            
            _logger.LogInformation("Found swarm manager: {ManagerName}", swarmManager.Name);
            
            // Get join tokens from the manager
            var (workerToken, _) = await dockerService.GetJoinTokensAsync(swarmManager);
            
            if (string.IsNullOrEmpty(workerToken))
            {
                _logger.LogWarning("Could not retrieve worker join token from swarm manager");
                return;
            }
            
            // Determine the manager's reachable address
            var managerHost = await DetermineManagerAddressAsync(swarmManager, sshService, dockerService);
            
            if (string.IsNullOrEmpty(managerHost))
            {
                _logger.LogError("Could not determine manager address for joining swarm");
                return;
            }
            
            // Remove stale nodes before joining
            await RemoveStaleNodesAsync(server);
            
            // Join the swarm
            var managerAddress = $"{managerHost}:2377";
            var joinCommand = $"docker swarm join --token {workerToken} {managerAddress}";
            
            _logger.LogInformation("Joining {ServerName} to swarm at {ManagerAddress}", server.Name, managerAddress);
            
            var result = await sshService.ExecuteCommandAsync(server, joinCommand);
            
            if (result.ExitCode == 0 || result.Output.Contains("This node joined a swarm"))
            {
                _logger.LogInformation("Successfully joined {ServerName} to swarm after auto-configure", server.Name);

                // Store the manager address and join token for future auto-rejoin
                server.SwarmManagerAddress = managerAddress;
                server.SwarmJoinToken = workerToken;
                server.IsSwarmWorker = true;
                server.Type = ServerType.SwarmWorker;
                await serverRepository.UpdateAsync(server);
            }
            else if (result.Output.Contains("This node is already part of a swarm"))
            {
                _logger.LogInformation("{ServerName} is already part of the swarm", server.Name);
            }
            else
            {
                _logger.LogError("Failed to join swarm after auto-configure: {Output}", result.Output + result.Error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error joining server {ServerName} to swarm after auto-configure", server.Name);
            // Don't fail the auto-configure if swarm join fails
        }
    }
    
    private async Task<string?> DetermineManagerAddressAsync(
        Server swarmManager,
        ISshService sshService,
        IDockerService dockerService)
    {
        var managerHost = swarmManager.Host;
        
        // If manager Host is localhost, we need to get its actual external IP
        if (managerHost.Equals("localhost", StringComparison.OrdinalIgnoreCase) || 
            managerHost == "127.0.0.1" || 
            managerHost == "::1")
        {
            _logger.LogInformation("Manager is localhost, detecting external IPv4 address...");
            
            // Try stored SwarmManagerAddress first
            if (!string.IsNullOrEmpty(swarmManager.SwarmManagerAddress))
            {
                managerHost = swarmManager.SwarmManagerAddress.Replace(":2377", "");
                _logger.LogInformation("Using stored manager address: {ManagerIp}", managerHost);
                return managerHost;
            }
            
            // Query Docker API for advertise address
            try
            {
                var dockerUri = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? "npipe://./pipe/docker_engine"
                    : "unix:///var/run/docker.sock";
                
                using var client = new DockerClientConfiguration(new Uri(dockerUri)).CreateClient();
                var nodes = await client.Swarm.ListNodesAsync();
                var managerNode = nodes.FirstOrDefault(n => n.Spec?.Role == "manager" && n.ManagerStatus?.Leader == true);
                
                if (managerNode?.Status?.Addr != null)
                {
                    managerHost = managerNode.Status.Addr;
                    _logger.LogInformation("Detected manager advertise address from Docker API: {ManagerIp}", managerHost);
                    return managerHost;
                }
                else
                {
                    _logger.LogError("Could not determine swarm manager advertise address from Docker API");
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting swarm advertise address from Docker API");
                return null;
            }
        }
        
        return managerHost;
    }
    
    private async Task RemoveStaleNodesAsync(Server server)
    {
        try
        {
            _logger.LogInformation("Checking for stale swarm nodes for {ServerName}...", server.Name);
            
            var dockerUri = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "npipe://./pipe/docker_engine"
                : "unix:///var/run/docker.sock";
            
            using var client = new DockerClientConfiguration(new Uri(dockerUri)).CreateClient();
            var allNodes = await client.Swarm.ListNodesAsync();
            
            // Find nodes with matching hostname or IP that are down
            var staleNodes = allNodes.Where(n => 
                (n.Description?.Hostname?.Equals(server.Name, StringComparison.OrdinalIgnoreCase) == true ||
                 n.Status?.Addr?.Equals(server.Host, StringComparison.OrdinalIgnoreCase) == true) &&
                n.Status?.State == "down").ToList();
            
            foreach (var staleNode in staleNodes)
            {
                _logger.LogInformation("Removing stale node {NodeId} ({Hostname}) from swarm", 
                    staleNode.ID, staleNode.Description?.Hostname);
                try
                {
                    await client.Swarm.RemoveNodeAsync(staleNode.ID, force: true);
                    _logger.LogInformation("Successfully removed stale node {NodeId}", staleNode.ID);
                }
                catch (Exception removeEx)
                {
                    _logger.LogWarning(removeEx, "Failed to remove stale node {NodeId}, continuing anyway", staleNode.ID);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during stale node cleanup, continuing with join");
        }
    }
}
