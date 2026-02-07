namespace HostCraft.Api.Models.Applications;

public record ServerResponseDto(int Id, string Name, string Host, int Port, string User, bool IsSwarm, string Status);
