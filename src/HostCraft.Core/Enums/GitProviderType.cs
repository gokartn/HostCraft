namespace HostCraft.Core.Enums;

/// <summary>
/// Supported Git providers for repository authentication and access.
/// </summary>
public enum GitProviderType
{
    /// <summary>
    /// GitHub.com or GitHub Enterprise
    /// </summary>
    GitHub = 0,
    
    /// <summary>
    /// GitLab.com or self-hosted GitLab
    /// </summary>
    GitLab = 1,
    
    /// <summary>
    /// Bitbucket Cloud or Bitbucket Server
    /// </summary>
    Bitbucket = 2,
    
    /// <summary>
    /// Self-hosted Gitea instance
    /// </summary>
    Gitea = 3
}
