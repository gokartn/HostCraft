using System.Text;
using HostCraft.Core.Entities;
using HostCraft.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Renci.SshNet;

namespace HostCraft.Infrastructure.Docker;

/// <summary>
/// Implementation of Docker Stack deployment using docker CLI via SSH.
/// Uses 'docker stack deploy' commands for managing multi-service applications.
/// </summary>
public class StackService : IStackService
{
    private readonly IDockerService _dockerService;
    private readonly ILogger<StackService> _logger;
    
    public StackService(
        IDockerService dockerService,
        ILogger<StackService> logger)
    {
        _dockerService = dockerService;
        _logger = logger;
    }
    
    public async Task<StackDeploymentResult> DeployStackAsync(
        Server server, 
        string stackName, 
        string composeYaml, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Deploying stack {StackName} to server {ServerHost}", 
                stackName, server.Host);
            
            // Validate stack name (must be lowercase alphanumeric with hyphens/underscores)
            if (!IsValidStackName(stackName))
            {
                return new StackDeploymentResult(
                    false, 
                    "Invalid stack name. Use only lowercase letters, numbers, hyphens, and underscores.", 
                    0,
                    "Invalid stack name format");
            }
            
            // Check if swarm is active
            var isSwarmActive = await _dockerService.IsSwarmActiveAsync(server, cancellationToken);
            if (!isSwarmActive)
            {
                return new StackDeploymentResult(
                    false, 
                    "Docker Swarm is not active on this server", 
                    0,
                    "Swarm not initialized");
            }
            
            // Deploy via SSH command
            var result = await DeployStackViaSshAsync(server, stackName, composeYaml);
            
            if (result.Success)
            {
                // Count services in the stack
                var services = await ListStackServicesAsync(server, stackName);
                
                return new StackDeploymentResult(
                    true,
                    $"Stack {stackName} deployed successfully",
                    services.Count());
            }
            else
            {
                return new StackDeploymentResult(
                    false,
                    "Stack deployment failed",
                    0,
                    result.Error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deploy stack {StackName}", stackName);
            return new StackDeploymentResult(
                false, 
                "Stack deployment failed", 
                0, 
                ex.Message);
        }
    }
    
    private async Task<(bool Success, string? Error)> DeployStackViaSshAsync(
        Server server, 
        string stackName, 
        string composeYaml)
    {
        // Create a temporary file path on the remote server
        var tempFile = $"/tmp/hostcraft-stack-{stackName}-{Guid.NewGuid()}.yml";
        
        try
        {
            using var sshClient = CreateSshClient(server);
            sshClient.Connect();
            
            // Write compose file to remote server
            using (var sftp = new SftpClient(server.Host, server.Port, server.Username, GetPrivateKey(server)))
            {
                sftp.Connect();
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(composeYaml));
                sftp.UploadFile(stream, tempFile);
                sftp.Disconnect();
            }
            
            // Deploy stack
            var deployCommand = sshClient.CreateCommand($"docker stack deploy -c {tempFile} {stackName}");
            var output = await Task.Run(() => deployCommand.Execute());
            
            // Clean up temporary file
            var cleanupCommand = sshClient.CreateCommand($"rm -f {tempFile}");
            cleanupCommand.Execute();
            
            sshClient.Disconnect();
            
            if (deployCommand.ExitStatus == 0)
            {
                _logger.LogInformation("Stack {StackName} deployed successfully. Output: {Output}", 
                    stackName, output);
                return (true, null);
            }
            else
            {
                var error = string.IsNullOrEmpty(deployCommand.Error) ? output : deployCommand.Error;
                _logger.LogError("Failed to deploy stack {StackName}. Error: {Error}", 
                    stackName, error);
                return (false, error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SSH error while deploying stack {StackName}", stackName);
            return (false, ex.Message);
        }
    }
    
    public async Task<bool> RemoveStackAsync(
        Server server, 
        string stackName, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Removing stack {StackName} from server {ServerHost}", 
                stackName, server.Host);
            
            using var sshClient = CreateSshClient(server);
            sshClient.Connect();
            
            var command = sshClient.CreateCommand($"docker stack rm {stackName}");
            var output = await Task.Run(() => command.Execute(), cancellationToken);
            
            sshClient.Disconnect();
            
            if (command.ExitStatus == 0)
            {
                _logger.LogInformation("Stack {StackName} removed successfully", stackName);
                return true;
            }
            else
            {
                _logger.LogError("Failed to remove stack {StackName}. Error: {Error}", 
                    stackName, command.Error);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove stack {StackName}", stackName);
            return false;
        }
    }
    
    public async Task<IEnumerable<StackInfo>> ListStacksAsync(
        Server server, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var sshClient = CreateSshClient(server);
            sshClient.Connect();
            
            // Get all services with stack labels
            var services = await _dockerService.ListServicesAsync(server, cancellationToken);
            
            // Group by stack namespace
            var stacks = services
                .Where(s => s.Name.Contains('_')) // Stack services have namespace_servicename format
                .Select(s => s.Name.Split('_')[0])
                .Distinct()
                .Select(stackName => new StackInfo(
                    stackName,
                    services.Count(s => s.Name.StartsWith(stackName + "_")),
                    DateTime.UtcNow // Would need to query individual services for actual creation time
                ))
                .ToList();
            
            sshClient.Disconnect();
            
            return stacks;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list stacks on server {ServerHost}", server.Host);
            return Enumerable.Empty<StackInfo>();
        }
    }
    
    public async Task<StackDetails?> InspectStackAsync(
        Server server, 
        string stackName, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            var services = await ListStackServicesAsync(server, stackName);
            var networks = await ListStackNetworksAsync(server, stackName);
            
            if (!services.Any())
            {
                return null;
            }
            
            return new StackDetails(
                stackName,
                services.Count(),
                services.ToList(),
                networks.ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to inspect stack {StackName}", stackName);
            return null;
        }
    }
    
    private async Task<IEnumerable<string>> ListStackServicesAsync(
        Server server, 
        string stackName)
    {
        try
        {
            using var sshClient = CreateSshClient(server);
            sshClient.Connect();
            
            var command = sshClient.CreateCommand($"docker stack services {stackName} --format '{{{{.Name}}}}'");
            var output = await Task.Run(() => command.Execute());
            
            sshClient.Disconnect();
            
            if (command.ExitStatus == 0 && !string.IsNullOrWhiteSpace(output))
            {
                return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.Trim());
            }
            
            return Enumerable.Empty<string>();
        }
        catch
        {
            return Enumerable.Empty<string>();
        }
    }
    
    private async Task<IEnumerable<string>> ListStackNetworksAsync(
        Server server, 
        string stackName)
    {
        try
        {
            var networks = await _dockerService.ListNetworksAsync(server);
            
            return networks
                .Where(n => n.Name.StartsWith(stackName + "_"))
                .Select(n => n.Name);
        }
        catch
        {
            return Enumerable.Empty<string>();
        }
    }
    
    private bool IsValidStackName(string stackName)
    {
        // Stack names must be lowercase alphanumeric with hyphens/underscores
        return !string.IsNullOrWhiteSpace(stackName) && 
               stackName.All(c => char.IsLower(c) || char.IsDigit(c) || c == '-' || c == '_');
    }
    
    private SshClient CreateSshClient(Server server)
    {
        var connectionInfo = new ConnectionInfo(
            server.Host,
            server.Port,
            server.Username,
            new PrivateKeyAuthenticationMethod(server.Username, GetPrivateKey(server)));
        
        return new SshClient(connectionInfo);
    }
    
    private PrivateKeyFile GetPrivateKey(Server server)
    {
        if (server.PrivateKey?.KeyData == null)
        {
            throw new InvalidOperationException($"No SSH private key configured for server {server.Name}");
        }
        
        using var keyStream = new MemoryStream(Encoding.UTF8.GetBytes(server.PrivateKey.KeyData));
        return new PrivateKeyFile(keyStream);
    }
}
