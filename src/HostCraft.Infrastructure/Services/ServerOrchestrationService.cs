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

public class ServerOrchestrationService : IServerOrchestrationService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ServerOrchestrationService> _logger;
    
    public ServerOrchestrationService(
        IServiceScopeFactory scopeFactory,
        ILogger<ServerOrchestrationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }
    
    public async Task ValidateAndConfigureServerAsync(int serverId)
    {
        using var scope = _scopeFactory.CreateScope();
        var serverRepository = scope.ServiceProvider.GetRequiredService<IServerRepository>();
        var dockerService = scope.ServiceProvider.GetRequiredService<IDockerService>();
        var proxyService = scope.ServiceProvider.GetRequiredService<IProxyService>();
        var sshService = scope.ServiceProvider.GetRequiredService<ISshService>();
        
        try
        {
            await Task.Delay(1000);
            
            var server = await serverRepository.GetByIdWithPrivateKeyAsync(serverId);
            
            if (server == null)
            {
                _logger.LogWarning("Server {ServerId} not found during validation", serverId);
                return;
            }
            
            // Validate Docker connection
            var isValid = await dockerService.ValidateConnectionAsync(server);
            server.Status = isValid ? ServerStatus.Online : ServerStatus.Offline;
            server.LastHealthCheck = DateTime.UtcNow;
            
            await serverRepository.UpdateAsync(server);
            
            // If server is online and marked as SwarmWorker, join it to swarm
            if (server.Type == ServerType.SwarmWorker && server.Status == ServerStatus.Online)
            {
                await JoinWorkerToSwarmInternalAsync(server, serverRepository, dockerService, sshService);
            }
            
            // Deploy reverse proxy if configured and online
            if (server.ProxyType != ProxyType.None && server.Status == ServerStatus.Online)
            {
                _logger.LogInformation("Deploying {ProxyType} on server {ServerName}", 
                    server.ProxyType, server.Name);
                
                await proxyService.EnsureProxyDeployedAsync(server);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background validation failed for server {ServerId}", serverId);
            
            var server = await serverRepository.GetByIdAsync(serverId);
            if (server != null)
            {
                server.Status = ServerStatus.Error;
                await serverRepository.UpdateAsync(server);
            }
        }
    }
    
    public async Task RevalidateServerAsync(int serverId)
    {
        using var scope = _scopeFactory.CreateScope();
        var serverRepository = scope.ServiceProvider.GetRequiredService<IServerRepository>();
        var dockerService = scope.ServiceProvider.GetRequiredService<IDockerService>();
        
        try
        {
            await Task.Delay(1000);
            
            var server = await serverRepository.GetByIdWithPrivateKeyAsync(serverId);
            
            if (server == null)
            {
                _logger.LogWarning("Server {ServerId} not found during revalidation", serverId);
                return;
            }
            
            _logger.LogInformation("Re-validating server {ServerName} ({ServerId})", server.Name, serverId);
            
            var isValid = await dockerService.ValidateConnectionAsync(server);
            server.Status = isValid ? ServerStatus.Online : ServerStatus.Offline;
            server.LastHealthCheck = DateTime.UtcNow;
            
            // Detect Swarm mode if connection is valid
            if (isValid)
            {
                try
                {
                    var systemInfo = await dockerService.GetSystemInfoAsync(server);
                    if (systemInfo.SwarmActive && server.Type == ServerType.Standalone)
                    {
                        _logger.LogInformation("Swarm detected on server {ServerName}, updating type to SwarmManager", server.Name);
                        server.Type = ServerType.SwarmManager;
                        server.IsSwarmManager = true;
                    }
                }
                catch (Exception swarmEx)
                {
                    _logger.LogWarning(swarmEx, "Failed to detect Swarm on server {ServerName}", server.Name);
                }
            }
            
            await serverRepository.UpdateAsync(server);
            
            _logger.LogInformation("Server {ServerName} validation result: {Status}", server.Name, server.Status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background re-validation failed for server {ServerId}", serverId);
            
            var server = await serverRepository.GetByIdAsync(serverId);
            if (server != null)
            {
                server.Status = ServerStatus.Error;
                await serverRepository.UpdateAsync(server);
            }
        }
    }
    
    public async Task<bool> JoinWorkerToSwarmAsync(Server worker, Server manager)
    {
        using var scope = _scopeFactory.CreateScope();
        var serverRepository = scope.ServiceProvider.GetRequiredService<IServerRepository>();
        var dockerService = scope.ServiceProvider.GetRequiredService<IDockerService>();
        var sshService = scope.ServiceProvider.GetRequiredService<ISshService>();
        
        try
        {
            // Check if worker is already part of a swarm - if yes, leave it first
            var workerSystemInfo = await dockerService.GetSystemInfoAsync(worker);
            if (workerSystemInfo.SwarmActive)
            {
                _logger.LogInformation("Server {ServerName} is already part of a swarm, leaving it first", worker.Name);
                
                try
                {
                    await dockerService.LeaveSwarmAsync(worker, force: true);
                    _logger.LogInformation("Successfully left old swarm");
                    await Task.Delay(2000);
                }
                catch (Exception leaveEx)
                {
                    _logger.LogWarning(leaveEx, "Failed to leave old swarm, will attempt join anyway");
                }
            }
            
            // Remove stale nodes
            await RemoveStaleSwarmNodesAsync(worker);
            
            // Get manager's advertise address
            var nodes = await dockerService.ListNodesAsync(manager);
            var managerNode = nodes.FirstOrDefault(n => n.Role == "manager" && n.Availability == "active");
            var managerAddress = managerNode?.Address ?? $"{manager.Host}:2377";
            
            _logger.LogInformation("Using swarm manager address: {Address} for {ManagerName}", 
                managerAddress, manager.Name);
            
            // Get worker join token
            var (workerToken, _) = await dockerService.GetJoinTokensAsync(manager);
            
            if (string.IsNullOrEmpty(workerToken))
            {
                _logger.LogError("Failed to get worker join token from manager {ManagerName}", manager.Name);
                return false;
            }
            
            // Execute join command on the worker
            var joinCommand = $"docker swarm join --token {workerToken} {managerAddress}";
            
            _logger.LogInformation("Executing swarm join on {ServerName} to manager {ManagerAddress}", 
                worker.Name, managerAddress);
            var result = await sshService.ExecuteCommandAsync(worker, joinCommand);
            
            if (result.ExitCode == 0)
            {
                _logger.LogInformation("Successfully joined {ServerName} to swarm", worker.Name);
                
                // Update worker with swarm info
                var dbWorker = await serverRepository.GetByIdAsync(worker.Id);
                if (dbWorker != null)
                {
                    dbWorker.SwarmNodeState = "ready";
                    dbWorker.SwarmNodeAvailability = "active";
                    dbWorker.SwarmJoinToken = workerToken;
                    dbWorker.SwarmManagerAddress = managerAddress;
                    dbWorker.IsSwarmWorker = true;
                    dbWorker.Type = ServerType.SwarmWorker;
                    await serverRepository.UpdateAsync(dbWorker);
                }
                
                return true;
            }
            else
            {
                _logger.LogError("Failed to join swarm: {Output}", result.Output + result.Error);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error joining server {ServerName} to swarm", worker.Name);
            return false;
        }
    }
    
    public async Task RemoveStaleSwarmNodesAsync(Server server)
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
            _logger.LogWarning(ex, "Error during stale node cleanup, continuing");
        }
    }
    
    private async Task JoinWorkerToSwarmInternalAsync(
        Server worker,
        IServerRepository serverRepository,
        IDockerService dockerService,
        ISshService sshService)
    {
        _logger.LogInformation("Server {ServerName} marked as SwarmWorker, attempting to join swarm", worker.Name);
        
        // Wait for swarm detection
        await Task.Delay(2000);
        
        // Find an active swarm manager
        Server? manager = null;
        for (int retry = 0; retry < 3 && manager == null; retry++)
        {
            if (retry > 0) await Task.Delay(2000);
            var managers = await serverRepository.GetSwarmManagersAsync();
            manager = managers.FirstOrDefault(s => s.Status == ServerStatus.Online);
        }
        
        if (manager == null)
        {
            _logger.LogWarning("No active swarm manager found to join {ServerName}", worker.Name);
            return;
        }
        
        try
        {
            await JoinWorkerToSwarmAsync(worker, manager);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error joining server {ServerName} to swarm", worker.Name);
        }
    }
}
