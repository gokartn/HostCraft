using HostCraft.Core.Entities;

namespace HostCraft.Core.Interfaces;

/// <summary>
/// Service for Git repository operations.
/// </summary>
public interface IGitService
{
    Task<string> CloneRepositoryAsync(string repositoryUrl, string? branch = null, string? username = null, string? token = null, CancellationToken cancellationToken = default);
    Task<string> GetLatestCommitHashAsync(string repositoryPath, CancellationToken cancellationToken = default);
    Task<bool> PullLatestAsync(string repositoryPath, CancellationToken cancellationToken = default);
    Task<IEnumerable<string>> GetBranchesAsync(string repositoryUrl, string? username = null, string? token = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Clone a repository for an application with proper authentication.
    /// </summary>
    Task<string> CloneApplicationRepositoryAsync(Application application, string? commitSha = null);
    
    /// <summary>
    /// Get authenticated clone URL for an application's Git provider.
    /// </summary>
    Task<string> GetAuthenticatedCloneUrlAsync(Application application);
    
    /// <summary>
    /// Clean up a cloned repository directory.
    /// </summary>
    Task CleanupRepositoryAsync(string repositoryPath);
}
