using System.Text;
using Docker.DotNet.Models;
using HostCraft.Core.Configuration;
using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using HostCraft.Core.Interfaces;
using HostCraft.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HostCraft.Infrastructure.Docker;

/// <summary>
/// Implements Docker image building from source code with real-time log streaming.
/// </summary>
public class BuildService : IBuildService
{
    private readonly IDockerService _dockerService;
    private readonly HostCraftDbContext _context;
    private readonly IDeploymentLogService _logService;
    private readonly ILogger<BuildService> _logger;
    private readonly DockerRegistryOptions _registryOptions;

    public BuildService(
        IDockerService dockerService,
        HostCraftDbContext context,
        IDeploymentLogService logService,
        ILogger<BuildService> logger,
        IOptions<DockerRegistryOptions> registryOptions)
    {
        _dockerService = dockerService;
        _context = context;
        _logService = logService;
        _logger = logger;
        _registryOptions = registryOptions.Value;
    }

    public async Task<string> BuildImageAsync(
        Application application,
        string sourcePath,
        string? commitSha = null)
    {
        // Find the running deployment for this application
        var deployment = await _context.Deployments
            .FirstOrDefaultAsync(d =>
                d.ApplicationId == application.Id &&
                d.CommitSha == commitSha &&
                d.Status == Core.Enums.DeploymentStatus.Running);

        var deploymentId = deployment?.Id ?? 0;

        try
        {
            // Generate image name
            var imageTag = !string.IsNullOrEmpty(commitSha)
                ? commitSha.Substring(0, Math.Min(7, commitSha.Length))
                : "latest";

            var imageName = $"{application.ServiceName}:{imageTag}";

            _logger.LogInformation(
                "Building Docker image {ImageName} from {SourcePath}",
                imageName,
                sourcePath);

            // Prepare build context
            var buildContext = Path.Combine(sourcePath, application.BuildContext ?? ".");
            var dockerfilePath = Path.Combine(buildContext, application.Dockerfile ?? "Dockerfile");

            if (!File.Exists(dockerfilePath))
            {
                await _logService.AddLogAsync(deploymentId, $"Dockerfile not found at {dockerfilePath}", "Error");
                throw new FileNotFoundException($"Dockerfile not found at {dockerfilePath}");
            }

            // Parse build args
            var buildArgs = ParseBuildArgs(application.BuildArgs);

            // Log build start
            await _logService.AddLogAsync(deploymentId, $"Starting Docker build for {imageName}", "Info");
            await _logService.AddLogAsync(deploymentId, $"Build context: {buildContext}", "Info");
            await _logService.AddLogAsync(deploymentId, $"Dockerfile: {application.Dockerfile ?? "Dockerfile"}", "Info");

            if (buildArgs.Any())
            {
                await _logService.AddLogAsync(deploymentId, $"Build args: {string.Join(", ", buildArgs.Keys)}", "Info");
            }

            // Build image using Docker service with progress callback
            var server = application.Server;
            var buildRequest = new BuildImageRequest(
                Dockerfile: application.Dockerfile ?? "Dockerfile",
                Context: buildContext,
                Tag: imageName,
                BuildArgs: buildArgs,
                Target: application.DockerBuildTarget
            );

            // Create progress reporter that logs to database
            var buildProgress = new Progress<string>(async log =>
            {
                if (!string.IsNullOrWhiteSpace(log))
                {
                    // Parse Docker build output to determine log level
                    var level = "Info";
                    if (log.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                        log.Contains("failed", StringComparison.OrdinalIgnoreCase))
                    {
                        level = "Error";
                    }
                    else if (log.Contains("warning", StringComparison.OrdinalIgnoreCase))
                    {
                        level = "Warning";
                    }
                    else if (log.StartsWith("Step ", StringComparison.OrdinalIgnoreCase))
                    {
                        level = "Info";
                    }

                    await _logService.AddLogAsync(deploymentId, log, level);
                    _logger.LogDebug("Build: {Log}", log);
                }
            });

            await _dockerService.BuildImageAsync(server, buildRequest, buildProgress);

            await _logService.AddLogAsync(deploymentId, $"Successfully built image {imageName}", "Success");
            _logger.LogInformation("Successfully built image {ImageName}", imageName);

            // Push to registry if Swarm mode and registry is enabled
            if (_registryOptions.Enabled && server.Type == ServerType.SwarmManager)
            {
                try
                {
                    await _logService.AddLogAsync(deploymentId, "Pushing image to registry...", "Info");
                    _logger.LogInformation("Pushing image {ImageName} to registry for Swarm deployment", imageName);

                    // Use configured registry URL
                    var registryUrl = _registryOptions.Url;
                    var registryImageName = $"{registryUrl}/{_registryOptions.Namespace}/{imageName}";

                    await _logService.AddLogAsync(deploymentId, $"Tagging image as {registryImageName}", "Info");

                    // Tag image for registry
                    await _dockerService.TagImageAsync(server, imageName, registryImageName);

                    await _logService.AddLogAsync(deploymentId, $"Pushing to {registryUrl}...", "Info");

                    // Create progress reporter for push
                    var pushProgress = new Progress<string>(async log =>
                    {
                        if (!string.IsNullOrWhiteSpace(log))
                        {
                            await _logService.AddLogAsync(deploymentId, log, "Info");
                            _logger.LogDebug("Push: {Log}", log);
                        }
                    });

                    // Push to registry
                    var registryAuth = !string.IsNullOrEmpty(_registryOptions.Username)
                        ? new RegistryAuthConfig(registryUrl, _registryOptions.Username, _registryOptions.Password)
                        : null;

                    await _dockerService.PushImageAsync(server, registryImageName, pushProgress, registryAuth);

                    await _logService.AddLogAsync(deploymentId, $"Successfully pushed to registry: {registryImageName}", "Success");
                    _logger.LogInformation("Successfully pushed image {RegistryImage} to registry", registryImageName);

                    // Return the registry image name for Swarm deployment
                    return registryImageName;
                }
                catch (Exception ex)
                {
                    await _logService.AddLogAsync(deploymentId, $"Warning: Failed to push to registry: {ex.Message}", "Warning");
                    _logger.LogWarning(ex, "Failed to push image to registry, will use local image");
                    // Fall back to local image name if push fails
                }
            }

            return imageName;
        }
        catch (Exception ex)
        {
            await _logService.AddLogAsync(deploymentId, $"Build failed: {ex.Message}", "Error");
            _logger.LogError(ex, "Failed to build image for application {App}", application.Name);
            throw;
        }
    }

    public async Task<bool> PushImageAsync(
        string imageName,
        string registryUrl,
        string? username = null,
        string? password = null)
    {
        try
        {
            _logger.LogInformation("Pushing image {ImageName} to {Registry}", imageName, registryUrl);

            // Tag the image for the target registry
            var targetImage = $"{registryUrl}/{imageName}";

            // Get application and server for Docker client access
            var server = await _context.Servers.FirstOrDefaultAsync(s => s.Status == Core.Enums.ServerStatus.Online);
            if (server == null)
            {
                _logger.LogError("No online server available for image push");
                return false;
            }

            // Tag the image for the registry
            await TagImageAsync(server, imageName, targetImage);

            // Use SSH to execute docker push command for remote registries
            var sshClient = GetSshClientForServer(server);
            if (sshClient != null)
            {
                var loginCmd = !string.IsNullOrEmpty(username)
                    ? $"echo '{password}' | docker login {registryUrl} -u {username} --password-stdin && "
                    : "";
                var pushCmd = sshClient.CreateCommand($"{loginCmd}docker push {targetImage}");
                var result = pushCmd.Execute();

                if (pushCmd.ExitStatus != 0)
                {
                    _logger.LogError("Push failed: {Error}", pushCmd.Error);
                    return false;
                }
            }

            _logger.LogInformation("Successfully pushed image {ImageName} to {Registry}", imageName, registryUrl);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to push image {ImageName}", imageName);
            return false;
        }
    }

    public async Task<List<string>> GetBuildLogsAsync(int deploymentId)
    {
        // Fetch logs from database
        var logs = await _context.DeploymentLogs
            .Where(l => l.DeploymentId == deploymentId)
            .OrderBy(l => l.Timestamp)
            .Select(l => $"[{l.Timestamp:HH:mm:ss}] {l.Message}")
            .ToListAsync();

        return logs;
    }

    public async IAsyncEnumerable<string> StreamBuildLogsAsync(int deploymentId)
    {
        var lastId = 0;

        while (true)
        {
            var logs = await _context.DeploymentLogs
                .Where(l => l.DeploymentId == deploymentId && l.Id > lastId)
                .OrderBy(l => l.Id)
                .ToListAsync();

            foreach (var log in logs)
            {
                yield return $"[{log.Timestamp:HH:mm:ss}] {log.Message}";
                lastId = log.Id;
            }

            await Task.Delay(100); // Poll every 100ms

            // Check if deployment is finished
            var deployment = await _context.Deployments.FindAsync(deploymentId);
            if (deployment != null &&
                (deployment.Status == Core.Enums.DeploymentStatus.Success ||
                 deployment.Status == Core.Enums.DeploymentStatus.Failed))
            {
                // Get any remaining logs
                var remainingLogs = await _context.DeploymentLogs
                    .Where(l => l.DeploymentId == deploymentId && l.Id > lastId)
                    .OrderBy(l => l.Id)
                    .ToListAsync();

                foreach (var log in remainingLogs)
                {
                    yield return $"[{log.Timestamp:HH:mm:ss}] {log.Message}";
                }

                break;
            }
        }
    }

    private async Task TagImageAsync(Server server, string sourceImage, string targetImage)
    {
        var sshClient = GetSshClientForServer(server);
        if (sshClient != null)
        {
            var tagCmd = sshClient.CreateCommand($"docker tag {sourceImage} {targetImage}");
            tagCmd.Execute();
            if (tagCmd.ExitStatus != 0)
            {
                throw new InvalidOperationException($"Failed to tag image: {tagCmd.Error}");
            }
        }
        await Task.CompletedTask;
    }

    private Renci.SshNet.SshClient? GetSshClientForServer(Server server)
    {
        if (server.Host == "localhost" || server.Host == "127.0.0.1")
            return null;

        if (server.PrivateKey == null || string.IsNullOrEmpty(server.PrivateKey.KeyData))
            throw new InvalidOperationException($"No private key configured for server {server.Name}");

        var keyFile = new Renci.SshNet.PrivateKeyFile(
            new MemoryStream(Encoding.UTF8.GetBytes(server.PrivateKey.KeyData)));
        var authMethod = new Renci.SshNet.PrivateKeyAuthenticationMethod(server.Username, keyFile);
        var connectionInfo = new Renci.SshNet.ConnectionInfo(server.Host, server.Port, server.Username, authMethod);
        var sshClient = new Renci.SshNet.SshClient(connectionInfo);
        sshClient.Connect();
        return sshClient;
    }

    private Dictionary<string, string> ParseBuildArgs(string? buildArgs)
    {
        var result = new Dictionary<string, string>();

        if (string.IsNullOrEmpty(buildArgs))
            return result;

        // Parse format: KEY1=VALUE1,KEY2=VALUE2
        var pairs = buildArgs.Split(',', StringSplitOptions.RemoveEmptyEntries);

        foreach (var pair in pairs)
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2)
            {
                result[parts[0].Trim()] = parts[1].Trim();
            }
        }

        return result;
    }
}
