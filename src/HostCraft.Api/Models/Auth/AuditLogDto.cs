namespace HostCraft.Api.Models.Auth;

public class AuditLogDto
{
    public int Id { get; set; }
    public int? UserId { get; set; }
    public required string EventType { get; set; }
    public required string Description { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public bool IsSuccess { get; set; }
    public DateTime Timestamp { get; set; }
}
