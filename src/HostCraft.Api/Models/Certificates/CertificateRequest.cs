namespace HostCraft.Api.Models.Certificates;

public record CertificateRequest(int ApplicationId, string Domain, string Email);
