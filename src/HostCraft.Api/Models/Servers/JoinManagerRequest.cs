namespace HostCraft.Api.Models.Servers;

/// <summary>
/// Request body for joining a server as a manager to an existing swarm
/// </summary>
public record JoinManagerRequest(int ServerIdToJoin);
