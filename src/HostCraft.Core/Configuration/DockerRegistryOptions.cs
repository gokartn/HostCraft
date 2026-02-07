namespace HostCraft.Core.Configuration;

/// <summary>
/// Configuration options for Docker Registry integration.
/// </summary>
public class DockerRegistryOptions
{
    public const string SectionName = "Docker:Registry";

    /// <summary>
    /// Whether to use the container registry for image distribution.
    /// Default: true for Swarm deployments.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Registry URL (host:port or hostname).
    /// Example: "manager1.local:5000" or "10.0.0.1:5000"
    /// For Swarm mode, this should be the manager node where the registry runs.
    /// All nodes will be automatically configured to trust this as an insecure registry.
    /// </summary>
    public string Url { get; set; } = "localhost:5000";

    /// <summary>
    /// Namespace/prefix for images (default: hostcraft).
    /// Example: hostcraft/myapp:v1
    /// </summary>
    public string Namespace { get; set; } = "hostcraft";

    /// <summary>
    /// Whether the registry uses HTTPS (default: false for internal registry).
    /// If false, nodes will be configured to allow insecure access.
    /// </summary>
    public bool Secure { get; set; } = false;

    /// <summary>
    /// Optional username for registry authentication.
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Optional password for registry authentication.
    /// </summary>
    public string? Password { get; set; }
}
