using HostCraft.Core.Enums;

namespace HostCraft.Api.Models.Servers;

/// <summary>
/// DTO for server list endpoint - serializes Region as a string to avoid JSON deserialization issues
/// </summary>
public class ServerListDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; }
    public ServerType Type { get; set; }
    public ServerStatus Status { get; set; }
    public string? Region { get; set; }
    public int? SwarmManagerCount { get; set; }
    public int? SwarmWorkerCount { get; set; }
    public bool IsSwarmManager { get; set; }
    public bool IsSwarmWorker { get; set; }
    public string? SwarmNodeId { get; set; }
    public string? SwarmNodeState { get; set; }
    public string? SwarmNodeAvailability { get; set; }
    public ProxyType ProxyType { get; set; }
    public string? DefaultLetsEncryptEmail { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastHealthCheck { get; set; }
    public string? ActualHostname { get; set; }
    public bool IsWizardSetup { get; set; }
    public int? WizardStep { get; set; }
    public DateTime? WizardCompletedAt { get; set; }

    // Computed property to match client DTO
    public bool IsSwarm => Type == ServerType.SwarmManager || Type == ServerType.SwarmWorker;
}
