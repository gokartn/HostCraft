using System.Collections.Generic;

namespace HostCraft.Core.Models.Applications.Operations;

/// <summary>
/// Represents an unmanaged swarm service discovered during orphan scans.
/// </summary>
public record OrphanedServiceInfo
{
    public string ServiceId { get; init; } = string.Empty;
    public string ServiceName { get; init; } = string.Empty;
    public string Image { get; init; } = string.Empty;
    public int Replicas { get; init; }
    public int ServerId { get; init; }
    public string ServerName { get; init; } = string.Empty;
    public int? ApplicationId { get; init; }
    public Dictionary<string, string> Labels { get; init; } = new();
}
