using System.Collections.Concurrent;
using System.Text;
using Docker.DotNet;
using Docker.DotNet.Models;
using HostCraft.Core.Entities;
using HostCraft.Core.Interfaces;
using HostCraft.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HostCraft.Infrastructure.Docker;

/// <summary>
/// Implements Docker image building from source code.
/// </summary>
public class BuildService : IBuildService
{
    private readonly IDockerService _dockerService;
    private readonly HostCraftDbContext _context;
    private readonly ILogger<BuildService> _logger;
    private readonly ConcurrentDictionary<int, List<string>> _buildLogs = new();

    public BuildService(
        IDockerService dockerService,
        HostCraftDbContext context,
        ILogger<BuildService> logger)
    {
        _dockerService = dockerService;
        _context = context;
        _logger = logger;
    }

    public async Task<string> BuildImageAsync(
        Application application,
        string sourcePath,
        string? commitSha = null)
    {
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
            
            var imageName = $"{application.Name.ToLower().Replace(" ", "-")}:{imageTag}";

            _logger.LogInformation(
                "Building Docker image {ImageName} from {SourcePath}",
                imageName,
                sourcePath);

            // Prepare build context
            var buildContext = Path.Combine(sourcePath, application.BuildContext ?? ".");
            var dockerfilePath = Path.Combine(buildContext, application.Dockerfile ?? "Dockerfile");

            if (!File.Exists(dockerfilePath))
            {
                throw new FileNotFoundException($"Dockerfile not found at {dockerfilePath}");
            }

            // Parse build args
            var buildArgs = ParseBuildArgs(application.BuildArgs);

            // Create tar archive of build context
            var tarStream = await CreateTarArchiveAsync(buildContext);

            // Build image using Docker service
            // Note: We'll need to pass the Server entity to the Build method
            var server = application.Server;
            
            var buildParameters = new ImageBuildParameters
            {
                Tags = new List<string> { imageName },
                Dockerfile = application.Dockerfile ?? "Dockerfile",
                BuildArgs = buildArgs,
                NoCache = false,
                Remove = true, // Remove intermediate containers
                ForceRemove = true,
            };

            AddLog(deploymentId, $"Building image {imageName}...");
            AddLog(deploymentId, $"Build context: {buildContext}");
            AddLog(deploymentId, $"Dockerfile: {application.Dockerfile}");

            var buildProgress = new Progress<string>(log =>
            {
                AddLog(deploymentId, log);
                _logger.LogDebug("Build: {Log}", log);
            });

            // Use the Docker service to build the image
            var buildRequest = new BuildImageRequest(
                Dockerfile: application.Dockerfile ?? "Dockerfile",
                Context: buildContext,
                Tag: imageName,
                BuildArgs: buildArgs
            );

            await _dockerService.BuildImageAsync(server, buildRequest, buildProgress);

            AddLog(deploymentId, $"Successfully built image {imageName}");
            _logger.LogInformation("Successfully built image {ImageName}", imageName);

            return imageName;
        }
        catch (Exception ex)
        {
            AddLog(deploymentId, $"Build failed: {ex.Message}");
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

            // TODO: Implement image push logic
            // This would use DockerClient.Images.PushImageAsync with authentication

            await Task.CompletedTask;
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
        await Task.CompletedTask;
        
        if (_buildLogs.TryGetValue(deploymentId, out var logs))
        {
            return logs.ToList();
        }

        return new List<string>();
    }

    public async IAsyncEnumerable<string> StreamBuildLogsAsync(int deploymentId)
    {
        var lastIndex = 0;

        while (true)
        {
            if (_buildLogs.TryGetValue(deploymentId, out var logs))
            {
                for (int i = lastIndex; i < logs.Count; i++)
                {
                    yield return logs[i];
                    lastIndex = i + 1;
                }
            }

            await Task.Delay(100); // Poll every 100ms

            // Check if deployment is finished
            var deployment = await _context.Deployments.FindAsync(deploymentId);
            if (deployment != null && 
                (deployment.Status == Core.Enums.DeploymentStatus.Success || 
                 deployment.Status == Core.Enums.DeploymentStatus.Failed))
            {
                break;
            }
        }
    }

    private void AddLog(int deploymentId, string message)
    {
        if (deploymentId == 0) return;

        _buildLogs.AddOrUpdate(
            deploymentId,
            new List<string> { $"[{DateTime.UtcNow:HH:mm:ss}] {message}" },
            (key, existing) =>
            {
                existing.Add($"[{DateTime.UtcNow:HH:mm:ss}] {message}");
                return existing;
            });
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

    private async Task<Stream> CreateTarArchiveAsync(string sourceDirectory)
    {
        var tarStream = new MemoryStream();
        
        // TODO: Implement proper tar archive creation
        // For now, this is a placeholder. In production, you'd use a library like SharpZipLib
        // to create a proper tar archive of the build context.
        
        // Simple implementation that just includes the files
        // A proper implementation would:
        // 1. Create a tar archive with proper headers
        // 2. Respect .dockerignore
        // 3. Handle symlinks and permissions

        await Task.CompletedTask;
        return tarStream;
    }
}
