namespace HostCraft.Core.Entities;

/// <summary>
/// Represents a refresh token for JWT authentication.
/// </summary>
public class RefreshToken
{
    public int Id { get; set; }

    public required string Token { get; set; }

    public int UserId { get; set; }

    public User? User { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public string? CreatedByIp { get; set; }

    public DateTime? RevokedAt { get; set; }

    public string? RevokedByIp { get; set; }

    public string? ReplacedByToken { get; set; }

    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;

    public bool IsActive => !IsExpired && RevokedAt == null;

    public bool IsRevoked => RevokedAt != null;
}