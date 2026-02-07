namespace HostCraft.Core.Enums;

/// <summary>
/// Defines what should be included in a backup.
/// Can be combined as flags for granular backup control.
/// </summary>
[Flags]
public enum BackupScope
{
    /// <summary>
    /// Backup system configuration (users, settings, system config)
    /// </summary>
    SystemConfiguration = 1 << 0,

    /// <summary>
    /// Backup server definitions (Docker hosts, SSH keys, Swarm config)
    /// </summary>
    Servers = 1 << 1,

    /// <summary>
    /// Backup project and application definitions (config, env vars, Traefik labels, deployment settings)
    /// </summary>
    ApplicationConfigurations = 1 << 2,

    /// <summary>
    /// Backup application volumes and persistent data
    /// </summary>
    ApplicationData = 1 << 3,

    /// <summary>
    /// Backup database volumes and data
    /// </summary>
    DatabaseData = 1 << 4,

    /// <summary>
    /// Backup Docker networks and Swarm overlay configurations
    /// </summary>
    DockerNetworks = 1 << 5,

    /// <summary>
    /// Backup SSL certificates (Let's Encrypt, custom certificates)
    /// </summary>
    Certificates = 1 << 6,

    /// <summary>
    /// Backup Git provider integrations and webhook configs
    /// </summary>
    GitIntegrations = 1 << 7,

    /// <summary>
    /// Backup secrets and encrypted credentials
    /// </summary>
    Secrets = 1 << 8,

    /// <summary>
    /// Backup deployment history and logs
    /// </summary>
    DeploymentHistory = 1 << 9,

    /// <summary>
    /// Complete backup - everything
    /// </summary>
    Complete = SystemConfiguration | Servers | ApplicationConfigurations |
               ApplicationData | DatabaseData | DockerNetworks |
               Certificates | GitIntegrations | Secrets | DeploymentHistory
}
