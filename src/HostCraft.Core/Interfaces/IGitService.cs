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
}
