using HostCraft.Core.Entities;

namespace HostCraft.Core.Interfaces;

/// <summary>
/// Service for building Docker images from source code.
/// </summary>
public interface IBuildService
{
    /// <summary>
    /// Build a Docker image from a Git repository source.
    /// </summary>
    /// <param name="application">Application to build</param>
    /// <param name="sourcePath">Path to source code</param>
    /// <param name="commitSha">Commit SHA to tag image with</param>
    /// <returns>Built image name with tag</returns>
    Task<string> BuildImageAsync(Application application, string sourcePath, string? commitSha = null);

    /// <summary>
    /// Push an image to a registry.
    /// </summary>
    Task<bool> PushImageAsync(string imageName, string registryUrl, string? username = null, string? password = null);

    /// <summary>
    /// Get build logs for a deployment.
    /// </summary>
    Task<List<string>> GetBuildLogsAsync(int deploymentId);

    /// <summary>
    /// Stream build logs in real-time.
    /// </summary>
    IAsyncEnumerable<string> StreamBuildLogsAsync(int deploymentId);
}
