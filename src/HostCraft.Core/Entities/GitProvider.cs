namespace HostCraft.Core.Entities;

/// <summary>
/// Represents a connected Git provider (GitHub, GitLab, Bitbucket, Gitea) with OAuth credentials.
/// </summary>
public class GitProvider
{
    public int Id { get; set; }
    
    public int UserId { get; set; }
    
    public required GitProviderType Type { get; set; }
    
    public required string Name { get; set; } // User's display name for this connection
    
    public required string Username { get; set; } // Git provider username
    
    public string? AvatarUrl { get; set; }
    
    public string? Email { get; set; }
    
    /// <summary>
    /// OAuth access token (encrypted at rest)
    /// </summary>
    public required string AccessToken { get; set; }
    
    /// <summary>
    /// OAuth refresh token (encrypted at rest)
    /// </summary>
    public string? RefreshToken { get; set; }
    
    /// <summary>
    /// Token expiration time (null if token doesn't expire)
    /// </summary>
    public DateTime? TokenExpiresAt { get; set; }
    
    /// <summary>
    /// OAuth scopes granted
    /// </summary>
    public string? Scopes { get; set; }
    
    /// <summary>
    /// For self-hosted instances (GitLab, Gitea, GitHub Enterprise)
    /// </summary>
    public string? ApiUrl { get; set; }
    
    /// <summary>
    /// Provider-specific user ID
    /// </summary>
    public string? ProviderId { get; set; }
    
    public DateTime ConnectedAt { get; set; }
    
    public DateTime? LastSyncAt { get; set; }
    
    public bool IsActive { get; set; } = true;
    
    // Navigation properties
    public User User { get; set; } = null!;
    
    public ICollection<Application> Applications { get; set; } = new List<Application>();
}
