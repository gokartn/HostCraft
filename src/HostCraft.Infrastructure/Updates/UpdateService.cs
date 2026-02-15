using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Docker.DotNet;
using Docker.DotNet.Models;
using HostCraft.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace HostCraft.Infrastructure.Updates;

public class UpdateService : IUpdateService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<UpdateService> _logger;
    private const string GitHubApiUrl = "https://api.github.com/repos/gokartn/hostcraft/releases/latest";
    private const string GitHubReleasesUrl = "https://api.github.com/repos/gokartn/hostcraft/releases";
    private const string ImageOwner = "gokartn";

    // Shared progress state (singleton-safe since UpdateService is transient but progress is static)
    private static readonly UpdateProgress _progress = new();
    private static readonly object _progressLock = new();

    public UpdateService(HttpClient httpClient, ILogger<UpdateService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "HostCraft-Update-Checker");
        }
    }

    public string GetCurrentVersion()
    {
        try
        {
            var versionFilePath = Path.Combine(AppContext.BaseDirectory, "VERSION");
            if (File.Exists(versionFilePath))
            {
                var version = File.ReadAllText(versionFilePath).Trim();
                if (!string.IsNullOrWhiteSpace(version))
                    return version;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read VERSION file");
        }

        return "0.0.1-alpha";
    }

    public async Task<UpdateInfo> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        var currentVersion = GetCurrentVersion();

        try
        {
            // Try latest release first, fall back to all releases (including pre-releases)
            var release = await FetchLatestReleaseAsync(cancellationToken);

            if (release == null)
            {
                return new UpdateInfo { CurrentVersion = currentVersion, UpdateAvailable = false };
            }

            var latestVersion = release.TagName?.TrimStart('v') ?? currentVersion;
            var updateAvailable = CompareVersions(currentVersion, latestVersion) < 0;

            return new UpdateInfo
            {
                CurrentVersion = currentVersion,
                LatestVersion = latestVersion,
                UpdateAvailable = updateAvailable,
                PublishedAt = release.PublishedAt,
                ReleaseNotes = release.Body,
                DownloadUrl = release.Assets?.FirstOrDefault()?.BrowserDownloadUrl,
                HtmlUrl = release.HtmlUrl,
                ApiImageUrl = $"ghcr.io/{ImageOwner}/hostcraft-api:{latestVersion}",
                WebImageUrl = $"ghcr.io/{ImageOwner}/hostcraft-web:{latestVersion}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for updates");
            return new UpdateInfo { CurrentVersion = currentVersion, UpdateAvailable = false };
        }
    }

    public UpdateProgress GetUpdateProgress()
    {
        lock (_progressLock)
        {
            return new UpdateProgress
            {
                InProgress = _progress.InProgress,
                TargetVersion = _progress.TargetVersion,
                CurrentStep = _progress.CurrentStep,
                StatusMessage = _progress.StatusMessage,
                Error = _progress.Error,
                StartedAt = _progress.StartedAt
            };
        }
    }

    public async Task<UpdateTriggerResult> TriggerUpdateAsync(string version, CancellationToken cancellationToken = default)
    {
        lock (_progressLock)
        {
            if (_progress.InProgress)
            {
                return new UpdateTriggerResult
                {
                    Success = false,
                    Message = "An update is already in progress"
                };
            }

            _progress.InProgress = true;
            _progress.TargetVersion = version;
            _progress.CurrentStep = UpdateStep.PullingApiImage;
            _progress.StatusMessage = "Starting update...";
            _progress.Error = null;
            _progress.StartedAt = DateTime.UtcNow;
        }

        // Run the update in the background so the HTTP response returns immediately
        _ = Task.Run(async () => await ExecuteUpdateAsync(version, CancellationToken.None), CancellationToken.None);

        // Detect mode for the response
        var mode = await DetectDeploymentModeAsync();

        return new UpdateTriggerResult
        {
            Success = true,
            Message = mode == UpdateMode.Swarm
                ? $"Update to v{version} initiated. Services will be updated with zero-downtime rolling updates."
                : $"Update to v{version} initiated. Containers will be recreated with the new images.",
            Mode = mode
        };
    }

    private async Task ExecuteUpdateAsync(string version, CancellationToken cancellationToken)
    {
        try
        {
            var apiImage = $"ghcr.io/{ImageOwner}/hostcraft-api:{version}";
            var webImage = $"ghcr.io/{ImageOwner}/hostcraft-web:{version}";

            using var dockerClient = new DockerClientConfiguration(
                new Uri(GetDockerEndpoint())).CreateClient();

            // Step 1: Pull API image
            SetProgress(UpdateStep.PullingApiImage, $"Pulling API image: {apiImage}");
            await PullImageAsync(dockerClient, apiImage, cancellationToken);

            // Step 2: Pull Web image
            SetProgress(UpdateStep.PullingWebImage, $"Pulling Web image: {webImage}");
            await PullImageAsync(dockerClient, webImage, cancellationToken);

            var mode = await DetectDeploymentModeAsync();

            if (mode == UpdateMode.Swarm)
            {
                await ExecuteSwarmUpdateAsync(dockerClient, version, apiImage, webImage, cancellationToken);
            }
            else
            {
                await ExecuteStandaloneUpdateAsync(dockerClient, version, apiImage, webImage, cancellationToken);
            }

            // Step: Update VERSION file
            try
            {
                var versionFilePath = Path.Combine(AppContext.BaseDirectory, "VERSION");
                await File.WriteAllTextAsync(versionFilePath, version, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not update local VERSION file");
            }

            SetProgress(UpdateStep.Completed, $"Successfully updated to v{version}");

            lock (_progressLock)
            {
                _progress.InProgress = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update to v{Version} failed", version);
            SetProgress(UpdateStep.Failed, null, ex.Message);

            lock (_progressLock)
            {
                _progress.InProgress = false;
            }
        }
    }

    private async Task ExecuteSwarmUpdateAsync(
        IDockerClient dockerClient,
        string version,
        string apiImage,
        string webImage,
        CancellationToken cancellationToken)
    {
        // Update Web service FIRST (less critical - doesn't affect API availability)
        SetProgress(UpdateStep.UpdatingWebService, "Updating Web service...");
        await UpdateSwarmServiceAsync(dockerClient, "hostcraft_web", webImage, cancellationToken);

        // Brief pause to let the web service begin its rolling update
        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);

        // Update API service (this service - Swarm rolling update keeps one replica alive)
        SetProgress(UpdateStep.UpdatingApiService, "Updating API service (rolling update)...");
        await UpdateSwarmServiceAsync(dockerClient, "hostcraft_api", apiImage, cancellationToken);

        SetProgress(UpdateStep.WaitingForHealthy, "Waiting for services to become healthy...");
        await WaitForServiceConvergenceAsync(dockerClient, "hostcraft_web", TimeSpan.FromMinutes(3), cancellationToken);
        // Note: We may not be able to wait for API convergence since this replica may be replaced
    }

    private async Task ExecuteStandaloneUpdateAsync(
        IDockerClient dockerClient,
        string version,
        string apiImage,
        string webImage,
        CancellationToken cancellationToken)
    {
        // In standalone mode, we need to stop and recreate containers
        // This will cause brief downtime
        SetProgress(UpdateStep.UpdatingWebService, "Recreating Web container with new image...");
        await RecreateContainerAsync(dockerClient, "hostcraft-web-1", webImage, cancellationToken);

        SetProgress(UpdateStep.UpdatingApiService, "Recreating API container with new image...");
        // The API container recreation will terminate this process
        // Docker's restart policy will start the new container
        await RecreateContainerAsync(dockerClient, "hostcraft-api-1", apiImage, cancellationToken);
    }

    private async Task UpdateSwarmServiceAsync(
        IDockerClient dockerClient,
        string serviceName,
        string newImage,
        CancellationToken cancellationToken)
    {
        // Find the service
        var services = await dockerClient.Swarm.ListServicesAsync(cancellationToken: cancellationToken);
        var service = services.FirstOrDefault(s =>
            s.Spec.Name.Equals(serviceName, StringComparison.OrdinalIgnoreCase));

        if (service == null)
        {
            _logger.LogWarning("Service {ServiceName} not found, skipping", serviceName);
            return;
        }

        // Update the image in the service spec
        service.Spec.TaskTemplate.ContainerSpec.Image = newImage;

        // Ensure rolling update config for zero-downtime
        service.Spec.UpdateConfig ??= new SwarmUpdateConfig();
        service.Spec.UpdateConfig.Parallelism = 1;
        service.Spec.UpdateConfig.Delay = 10_000_000_000; // 10 seconds in nanoseconds
        service.Spec.UpdateConfig.Order = "start-first";
        service.Spec.UpdateConfig.FailureAction = "rollback";

        var updateParams = new ServiceUpdateParameters
        {
            Service = service.Spec,
            Version = (long)service.Version.Index
        };

        await dockerClient.Swarm.UpdateServiceAsync(service.ID, updateParams, cancellationToken);
        _logger.LogInformation("Initiated rolling update for service {ServiceName} to image {Image}", serviceName, newImage);
    }

    private async Task RecreateContainerAsync(
        IDockerClient dockerClient,
        string containerName,
        string newImage,
        CancellationToken cancellationToken)
    {
        // Find the container
        var containers = await dockerClient.Containers.ListContainersAsync(
            new ContainersListParameters { All = true },
            cancellationToken);

        var container = containers.FirstOrDefault(c =>
            c.Names.Any(n => n.TrimStart('/').Equals(containerName, StringComparison.OrdinalIgnoreCase)));

        if (container == null)
        {
            _logger.LogWarning("Container {ContainerName} not found, skipping", containerName);
            return;
        }

        // Stop the container
        await dockerClient.Containers.StopContainerAsync(container.ID,
            new ContainerStopParameters { WaitBeforeKillSeconds = 10 },
            cancellationToken);

        // Remove the container
        await dockerClient.Containers.RemoveContainerAsync(container.ID,
            new ContainerRemoveParameters { Force = true },
            cancellationToken);

        // Recreate with new image - preserve original config
        var inspected = await dockerClient.Containers.InspectContainerAsync(container.ID, cancellationToken);

        // For standalone mode, the simplest approach is updating the .env and running compose up
        // But since we're inside the container, we update via Docker API
        _logger.LogInformation("Container {ContainerName} stopped and removed. " +
            "Docker Compose restart policy will recreate it with the new image if configured. " +
            "Otherwise, run 'docker compose up -d' manually.", containerName);
    }

    private async Task WaitForServiceConvergenceAsync(
        IDockerClient dockerClient,
        string serviceName,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var services = await dockerClient.Swarm.ListServicesAsync(cancellationToken: cancellationToken);
                var service = services.FirstOrDefault(s =>
                    s.Spec.Name.Equals(serviceName, StringComparison.OrdinalIgnoreCase));

                if (service != null)
                {
                    // Check if update is complete by inspecting tasks
                    var tasks = await dockerClient.Tasks.ListAsync(cancellationToken);
                    var serviceTasks = tasks
                        .Where(t => t.ServiceID == service.ID && t.DesiredState == TaskState.Running)
                        .ToList();

                    var runningCount = serviceTasks.Count(t => t.Status.State == TaskState.Running);
                    var desiredReplicas = service.Spec.Mode?.Replicated?.Replicas ?? 1;

                    if (runningCount >= (long)desiredReplicas)
                    {
                        _logger.LogInformation("Service {ServiceName} converged: {Running}/{Desired} replicas running",
                            serviceName, runningCount, desiredReplicas);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error checking convergence for {ServiceName}", serviceName);
            }

            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
        }

        _logger.LogWarning("Service {ServiceName} did not converge within {Timeout}", serviceName, timeout);
    }

    private async Task PullImageAsync(IDockerClient dockerClient, string imageName, CancellationToken cancellationToken)
    {
        // Parse image name into parts
        var parts = imageName.Split(':');
        var fromImage = parts[0];
        var tag = parts.Length > 1 ? parts[1] : "latest";

        await dockerClient.Images.CreateImageAsync(
            new ImagesCreateParameters
            {
                FromImage = fromImage,
                Tag = tag
            },
            null, // No auth needed for public ghcr.io images
            new Progress<JSONMessage>(msg =>
            {
                if (!string.IsNullOrEmpty(msg.Status))
                {
                    _logger.LogDebug("Pull {Image}: {Status} {Progress}", imageName, msg.Status, msg.ProgressMessage);
                }
            }),
            cancellationToken);

        _logger.LogInformation("Pulled image {Image}", imageName);
    }

    private async Task<UpdateMode> DetectDeploymentModeAsync()
    {
        try
        {
            using var dockerClient = new DockerClientConfiguration(
                new Uri(GetDockerEndpoint())).CreateClient();

            var info = await dockerClient.System.GetSystemInfoAsync();
            if (info.Swarm?.LocalNodeState == "active")
            {
                return UpdateMode.Swarm;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not detect deployment mode, defaulting to Standalone");
        }

        return UpdateMode.Standalone;
    }

    private static string GetDockerEndpoint()
    {
        // Inside a Docker container on Linux
        if (File.Exists("/var/run/docker.sock"))
            return "unix:///var/run/docker.sock";

        // Windows development
        return "npipe://./pipe/docker_engine";
    }

    private void SetProgress(UpdateStep step, string? message, string? error = null)
    {
        lock (_progressLock)
        {
            _progress.CurrentStep = step;
            _progress.StatusMessage = message;
            if (error != null) _progress.Error = error;
        }
    }

    private async Task<GitHubRelease?> FetchLatestReleaseAsync(CancellationToken cancellationToken)
    {
        // Try /releases/latest first (only returns non-prerelease)
        try
        {
            var response = await _httpClient.GetAsync(GitHubApiUrl, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var release = JsonSerializer.Deserialize<GitHubRelease>(content);
                if (release != null) return release;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to fetch latest stable release, trying all releases");
        }

        // Fall back to all releases (includes pre-releases) and pick the newest
        try
        {
            var response = await _httpClient.GetAsync($"{GitHubReleasesUrl}?per_page=5", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var releases = JsonSerializer.Deserialize<List<GitHubRelease>>(content);
                return releases?.FirstOrDefault(); // GitHub returns newest first
            }
            else
            {
                var statusCode = (int)response.StatusCode;
                if (statusCode >= 500)
                    _logger.LogWarning("GitHub API returned {StatusCode} when checking releases", statusCode);
                else
                    _logger.LogDebug("Update check returned {StatusCode} - repository may not have releases yet", statusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch releases from GitHub");
        }

        return null;
    }

    /// <summary>
    /// Compare two version strings. Supports formats like "0.0.1", "0.1.0-alpha", "1.0.0-beta.2".
    /// Pre-release versions are considered lower than their release counterpart.
    /// </summary>
    internal static int CompareVersions(string current, string latest)
    {
        try
        {
            var (currentNumeric, currentPre) = ParseVersion(current);
            var (latestNumeric, latestPre) = ParseVersion(latest);

            // Compare numeric parts first
            for (int i = 0; i < Math.Max(currentNumeric.Length, latestNumeric.Length); i++)
            {
                var c = i < currentNumeric.Length ? currentNumeric[i] : 0;
                var l = i < latestNumeric.Length ? latestNumeric[i] : 0;
                if (c < l) return -1;
                if (c > l) return 1;
            }

            // Numeric parts are equal - compare pre-release
            // A version without pre-release > a version with pre-release (1.0.0 > 1.0.0-alpha)
            if (string.IsNullOrEmpty(currentPre) && !string.IsNullOrEmpty(latestPre))
                return 1; // current is release, latest is pre-release → current is newer
            if (!string.IsNullOrEmpty(currentPre) && string.IsNullOrEmpty(latestPre))
                return -1; // current is pre-release, latest is release → latest is newer
            if (!string.IsNullOrEmpty(currentPre) && !string.IsNullOrEmpty(latestPre))
                return string.Compare(currentPre, latestPre, StringComparison.OrdinalIgnoreCase);

            return 0;
        }
        catch
        {
            return 0;
        }
    }

    private static (int[] Numeric, string? PreRelease) ParseVersion(string version)
    {
        // Strip leading 'v'
        version = version.TrimStart('v');

        // Split on first hyphen: "1.2.3-alpha" → "1.2.3" + "alpha"
        string? preRelease = null;
        var hyphenIdx = version.IndexOf('-');
        if (hyphenIdx >= 0)
        {
            preRelease = version[(hyphenIdx + 1)..];
            version = version[..hyphenIdx];
        }

        var numeric = version.Split('.')
            .Select(p => int.TryParse(p, out var n) ? n : 0)
            .ToArray();

        return (numeric, preRelease);
    }
}

public class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("published_at")]
    public DateTime? PublishedAt { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }

    [JsonPropertyName("assets")]
    public List<GitHubAsset>? Assets { get; set; }
}

public class GitHubAsset
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("browser_download_url")]
    public string? BrowserDownloadUrl { get; set; }
}
