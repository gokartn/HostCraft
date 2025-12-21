namespace HostCraft.Core.Entities;

/// <summary>
/// Represents an audit log entry for security events.
/// </summary>
public class AuditLog
{
    public int Id { get; set; }

    public string? UserId { get; set; } // Nullable for anonymous events

    public string? Username { get; set; }

    public string EventType { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public string? AdditionalData { get; set; } // JSON data

    public bool IsSuccess { get; set; }

    public DateTime Timestamp { get; set; }

    public string? SessionId { get; set; }
}