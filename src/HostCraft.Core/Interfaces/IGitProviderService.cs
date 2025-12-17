using HostCraft.Core.Entities;
using HostCraft.Core.Enums;

namespace HostCraft.Core.Interfaces;

/// <summary>
/// Service for managing Git provider connections and OAuth flows.
/// </summary>
public interface IGitProviderService
{
    /// <summary>
    /// Get OAuth authorization URL for a Git provider.
    /// </summary>
    Task<string> GetAuthorizationUrlAsync(GitProviderType type, string redirectUri, string? apiUrl = null);
    
    /// <summary>
    /// Exchange OAuth code for access token and save provider connection.
    /// </summary>
    Task<GitProvider> ConnectProviderAsync(GitProviderType type, string code, string redirectUri, int userId, string? apiUrl = null);
    
    /// <summary>
    /// Refresh an expired OAuth token.
    /// </summary>
    Task<bool> RefreshTokenAsync(int providerId);
    
    /// <summary>
    /// Register a webhook with the Git provider for an application.
    /// </summary>
    Task<bool> RegisterWebhookAsync(Application application, string webhookUrl, string webhookSecret);
    
    /// <summary>
    /// Unregister a webhook from the Git provider.
    /// </summary>
    Task<bool> UnregisterWebhookAsync(Application application);
    
    /// <summary>
    /// Get all repositories for a connected Git provider.
    /// </summary>
    Task<List<GitRepository>> GetRepositoriesAsync(int providerId);
    
    /// <summary>
    /// Get branches for a specific repository.
    /// </summary>
    Task<List<string>> GetBranchesAsync(int providerId, string owner, string repo);
    
    /// <summary>
    /// Get the latest commit for a branch.
    /// </summary>
    Task<GitCommit?> GetLatestCommitAsync(int providerId, string owner, string repo, string branch);
    
    /// <summary>
    /// Test if provider connection is still valid.
    /// </summary>
    Task<bool> TestConnectionAsync(int providerId);
    
    /// <summary>
    /// Disconnect and delete a Git provider connection.
    /// </summary>
    Task<bool> DisconnectProviderAsync(int providerId);
}

/// <summary>
/// Git repository information from provider API.
/// </summary>
public class GitRepository
{
    public required string Owner { get; set; }
    public required string Name { get; set; }
    public required string FullName { get; set; } // owner/name
    public string? Description { get; set; }
    public required string DefaultBranch { get; set; }
    public required string CloneUrl { get; set; }
    public bool IsPrivate { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Git commit information.
/// </summary>
public class GitCommit
{
    public required string Sha { get; set; }
    public required string Message { get; set; }
    public required string Author { get; set; }
    public DateTime CommittedAt { get; set; }
}
