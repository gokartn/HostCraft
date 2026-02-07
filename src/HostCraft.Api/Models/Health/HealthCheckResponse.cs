namespace HostCraft.Api.Models.Health;

public class HealthCheckResponse
{
    public int? Id { get; set; }
    public int? ApplicationId { get; set; }
    public int? ServerId { get; set; }
    public required string Status { get; set; }
    public int ResponseTimeMs { get; set; }
    public string? StatusCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CheckedAt { get; set; }
}
