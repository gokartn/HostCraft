namespace HostCraft.Api.Models.SystemSettings;

public record ContainerLogsResponse
{
    public string? WebLogs { get; init; }
    public string? ApiLogs { get; init; }
    public string? PostgresLogs { get; init; }
}
