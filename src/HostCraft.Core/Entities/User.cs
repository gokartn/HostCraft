namespace HostCraft.Core.Entities;

/// <summary>
/// Represents a user account in HostCraft.
/// </summary>
public class User
{
    public int Id { get; set; }

    public Guid Uuid { get; set; }

    public required string Email { get; set; }

    public required string PasswordHash { get; set; }

    public string? Name { get; set; }

    public bool IsAdmin { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? LastLoginAt { get; set; }

    // Security enhancements
    public bool IsLockedOut { get; set; }

    public DateTime? LockoutEnd { get; set; }

    public int AccessFailedCount { get; set; }

    public bool TwoFactorEnabled { get; set; }

    public string? TwoFactorSecret { get; set; }

    public string? RecoveryCodes { get; set; } // JSON array of recovery codes

    public bool EmailConfirmed { get; set; }

    public string? EmailConfirmationToken { get; set; }

    public DateTime? EmailConfirmationTokenExpiresAt { get; set; }

    public string? PasswordResetToken { get; set; }

    public DateTime? PasswordResetTokenExpiresAt { get; set; }

    public string? SecurityStamp { get; set; } // Changes when security-related info changes

    public DateTime? LastPasswordChangeAt { get; set; }
}
