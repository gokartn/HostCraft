namespace HostCraft.Api.Models.Nodes;

public record NodeDto(
    string Id,
    string Hostname,
    string Role,
    string State,
    string Availability,
    bool IsLeader,
    string Address,
    long NanoCPUs,
    long MemoryBytes,
    string EngineVersion,
    string Platform);
