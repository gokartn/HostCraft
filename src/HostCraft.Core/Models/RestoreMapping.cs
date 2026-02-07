namespace HostCraft.Core.Models;

/// <summary>
/// Maps backup configuration to new environment during restore.
/// Handles server-specific settings that must be adapted (IPs, hostnames, etc.)
/// </summary>
public class RestoreMapping
{
    /// <summary>
    /// Server mappings: old server ID → new server configuration
    /// </summary>
    public Dictionary<int, ServerRestoreMapping> ServerMappings { get; set; } = new();

    /// <summary>
    /// Domain mappings: old domain → new domain (for multi-environment deploys)
    /// </summary>
    public Dictionary<string, string> DomainMappings { get; set; } = new();

    /// <summary>
    /// IP address mappings: old IP → new IP
    /// </summary>
    public Dictionary<string, string> IpAddressMappings { get; set; } = new();

    /// <summary>
    /// Whether to regenerate secrets (recommended for new environment)
    /// </summary>
    public bool RegenerateSecrets { get; set; } = true;

    /// <summary>
    /// Whether to regenerate SSL certificates (if using Let's Encrypt, re-issue for new IPs)
    /// </summary>
    public bool RegenerateCertificates { get; set; } = false;

    /// <summary>
    /// Whether to preserve UUIDs (false = generate new UUIDs for all entities)
    /// </summary>
    public bool PreserveUuids { get; set; } = true;
}

/// <summary>
/// Mapping for restoring a server to a new instance
/// </summary>
public class ServerRestoreMapping
{
    /// <summary>
    /// Original server ID from backup
    /// </summary>
    public int OriginalServerId { get; set; }

    /// <summary>
    /// New SSH connection details
    /// </summary>
    public string? NewHostname { get; set; }
    public int? NewSshPort { get; set; }
    public string? NewSshUsername { get; set; }

    /// <summary>
    /// New public IP address (for Swarm advertise address, etc.)
    /// </summary>
    public string? NewPublicIp { get; set; }

    /// <summary>
    /// New Docker socket path (if different)
    /// </summary>
    public string? NewDockerSocketPath { get; set; }

    /// <summary>
    /// Should this server be skipped during restore?
    /// </summary>
    public bool Skip { get; set; } = false;

    /// <summary>
    /// Map to existing server ID (if merging with existing HostCraft instance)
    /// </summary>
    public int? MapToExistingServerId { get; set; }
}

/// <summary>
/// Configuration needed from user before restore can proceed
/// </summary>
public class RestoreRequiredInput
{
    /// <summary>
    /// Servers that need new connection details
    /// </summary>
    public List<ServerInputRequired> ServerInputs { get; set; } = new();

    /// <summary>
    /// Domains that need verification or remapping
    /// </summary>
    public List<DomainInputRequired> DomainInputs { get; set; } = new();

    /// <summary>
    /// IP addresses that need updating
    /// </summary>
    public List<IpAddressInputRequired> IpAddressInputs { get; set; } = new();
}

public class ServerInputRequired
{
    public int OriginalServerId { get; set; }
    public required string OriginalHostname { get; set; }
    public required string OriginalIpAddress { get; set; }
    public required string Prompt { get; set; }
    public bool IsRequired { get; set; } = true;
}

public class DomainInputRequired
{
    public required string OriginalDomain { get; set; }
    public required string UsedByApplications { get; set; } // Comma-separated app names
    public required string Prompt { get; set; }
    public bool CanSkip { get; set; } = false;
}

public class IpAddressInputRequired
{
    public required string OriginalIpAddress { get; set; }
    public required string UsedBy { get; set; } // What uses this IP (Swarm advertise, server connection, etc.)
    public required string Prompt { get; set; }
}
