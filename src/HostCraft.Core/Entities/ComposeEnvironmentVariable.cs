using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HostCraft.Core.Entities;

/// <summary>
/// Environment variable configuration for Docker Compose applications
/// </summary>
public class ComposeEnvironmentVariable
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Reference to the application this variable belongs to
    /// </summary>
    [Required]
    public int ApplicationId { get; set; }

    [ForeignKey(nameof(ApplicationId))]
    public Application Application { get; set; } = null!;

    /// <summary>
    /// Environment variable name (e.g., "DATABASE_URL", "API_KEY")
    /// </summary>
    [Required]
    [MaxLength(255)]
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Environment variable value (encrypted if IsSecret is true)
    /// </summary>
    [Required]
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Whether this variable contains sensitive data (passwords, API keys, etc.)
    /// If true, the value should be encrypted at rest
    /// </summary>
    public bool IsSecret { get; set; } = false;

    /// <summary>
    /// Optional description of what this variable is used for
    /// </summary>
    [MaxLength(500)]
    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
