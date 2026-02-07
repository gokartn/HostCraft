namespace HostCraft.Api.Models.Certificates;

public class CertificateDto
{
    public int Id { get; set; }
    public int ApplicationId { get; set; }
    public required string Domain { get; set; }
    public required string Provider { get; set; }
    public required string Status { get; set; }
    public DateTime? IssuedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool AutoRenew { get; set; }
    public string? ErrorMessage { get; set; }
}
