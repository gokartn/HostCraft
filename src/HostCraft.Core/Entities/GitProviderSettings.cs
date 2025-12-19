using HostCraft.Core.Enums;

namespace HostCraft.Core.Entities;

/// <summary>
/// Stores OAuth credentials for Git providers (GitHub, GitLab, Bitbucket, Gitea).
/// These are configured by the admin in the UI and stored in the database.
/// </summary>
public class GitProviderSettings
{
    public int Id { get; set; }

    /// <summary>
    /// The type of Git provider these settings are for.
    /// </summary>
    public GitProviderType Type { get; set; }

    /// <summary>
    /// Display name for this configuration (e.g., "GitHub" or "Self-hosted GitLab")
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// OAuth Client ID / App ID
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// OAuth Client Secret (encrypted at rest)
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// For self-hosted instances (GitLab, Gitea, GitHub Enterprise)
    /// </summary>
    public string? ApiUrl { get; set; }

    /// <summary>
    /// Whether this provider configuration is enabled
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Whether credentials have been configured
    /// </summary>
    public bool IsConfigured => !string.IsNullOrEmpty(ClientId) && !string.IsNullOrEmpty(ClientSecret);

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }
}
