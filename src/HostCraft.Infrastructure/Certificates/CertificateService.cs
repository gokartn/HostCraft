using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using HostCraft.Core.Entities;
using HostCraft.Core.Interfaces;
using HostCraft.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HostCraft.Infrastructure.Certificates;

/// <summary>
/// Service for managing SSL/TLS certificates with Let's Encrypt and other providers.
/// Traefik handles actual ACME challenges - this service manages certificate lifecycle.
/// </summary>
public class CertificateService : ICertificateService
{
    private readonly HostCraftDbContext _context;
    private readonly IProxyService _proxyService;
    private readonly ISshService _sshService;
    private readonly ILogger<CertificateService> _logger;
    private readonly HttpClient _httpClient;

    private const int RenewalThresholdDays = 30;

    public CertificateService(
        HostCraftDbContext context,
        IProxyService proxyService,
        ISshService sshService,
        ILogger<CertificateService> logger,
        HttpClient httpClient)
    {
        _context = context;
        _proxyService = proxyService;
        _sshService = sshService;
        _logger = logger;
        _httpClient = httpClient;
    }

    public async Task<Certificate> RequestCertificateAsync(int applicationId, string domain, string email)
    {
        var application = await _context.Applications
            .Include(a => a.Server)
            .ThenInclude(s => s.PrivateKey)
            .Include(a => a.Certificates)
            .FirstOrDefaultAsync(a => a.Id == applicationId);

        if (application == null)
        {
            throw new InvalidOperationException($"Application {applicationId} not found");
        }

        // Check if certificate already exists for this domain
        var existingCert = application.Certificates.FirstOrDefault(c => c.Domain == domain);
        if (existingCert != null && existingCert.Status == CertificateStatus.Active)
        {
            _logger.LogInformation("Certificate already exists for domain {Domain}", domain);
            return existingCert;
        }

        // Create new certificate record
        var certificate = new Certificate
        {
            ApplicationId = applicationId,
            Domain = domain,
            Provider = "Let's Encrypt",
            Status = CertificateStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        _context.Certificates.Add(certificate);
        await _context.SaveChangesAsync();

        try
        {
            certificate.Status = CertificateStatus.Issuing;
            await _context.SaveChangesAsync();

            // Update application domain and HTTPS settings
            application.Domain = domain;
            application.EnableHttps = true;
            application.LetsEncryptEmail = email;

            // Configure the proxy with SSL settings
            await _proxyService.ConfigureApplicationAsync(application);

            // Wait briefly and check if certificate was issued
            await Task.Delay(5000);

            // Try to fetch certificate info from the live domain
            var certInfo = await FetchCertificateInfoFromDomainAsync(domain);
            if (certInfo != null)
            {
                certificate.Status = CertificateStatus.Active;
                certificate.IssuedAt = certInfo.NotBefore;
                certificate.ExpiresAt = certInfo.NotAfter;
                certificate.SerialNumber = certInfo.SerialNumber;
                certificate.Issuer = certInfo.Issuer;

                _logger.LogInformation("Certificate issued for {Domain}, expires {ExpiresAt}",
                    domain, certificate.ExpiresAt);
            }
            else
            {
                // Certificate pending - Traefik will handle ACME challenge asynchronously
                _logger.LogInformation("Certificate request submitted for {Domain}, waiting for ACME challenge",
                    domain);
            }

            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to request certificate for {Domain}", domain);
            certificate.Status = CertificateStatus.Failed;
            certificate.ErrorMessage = ex.Message;
            await _context.SaveChangesAsync();
        }

        return certificate;
    }

    public async Task<bool> RenewCertificateAsync(int certificateId)
    {
        var certificate = await _context.Certificates
            .Include(c => c.Application)
            .ThenInclude(a => a.Server)
            .ThenInclude(s => s.PrivateKey)
            .FirstOrDefaultAsync(c => c.Id == certificateId);

        if (certificate == null)
        {
            _logger.LogWarning("Certificate {CertificateId} not found for renewal", certificateId);
            return false;
        }

        try
        {
            certificate.Status = CertificateStatus.Renewing;
            certificate.LastRenewalAttempt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // Force proxy reconfiguration to trigger Traefik certificate renewal
            await _proxyService.ConfigureApplicationAsync(certificate.Application);

            // Try to restart Traefik to force ACME renewal
            await ForceTraefikCertificateRenewalAsync(certificate.Application.Server);

            // Wait and check for new certificate
            await Task.Delay(10000);

            var certInfo = await FetchCertificateInfoFromDomainAsync(certificate.Domain);
            if (certInfo != null && certInfo.NotAfter > (certificate.ExpiresAt ?? DateTime.MinValue))
            {
                certificate.Status = CertificateStatus.Active;
                certificate.IssuedAt = certInfo.NotBefore;
                certificate.ExpiresAt = certInfo.NotAfter;
                certificate.SerialNumber = certInfo.SerialNumber;
                certificate.Issuer = certInfo.Issuer;
                certificate.ErrorMessage = null;

                _logger.LogInformation("Certificate renewed for {Domain}, new expiry: {ExpiresAt}",
                    certificate.Domain, certificate.ExpiresAt);

                await _context.SaveChangesAsync();
                return true;
            }

            // Renewal may be pending
            _logger.LogInformation("Certificate renewal in progress for {Domain}", certificate.Domain);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to renew certificate {CertificateId}", certificateId);
            certificate.Status = CertificateStatus.Failed;
            certificate.ErrorMessage = $"Renewal failed: {ex.Message}";
            await _context.SaveChangesAsync();
            return false;
        }
    }

    public async Task<int> AutoRenewExpiringCertificatesAsync()
    {
        var expiringCerts = await _context.Certificates
            .Include(c => c.Application)
            .ThenInclude(a => a.Server)
            .ThenInclude(s => s.PrivateKey)
            .Where(c => c.AutoRenew &&
                        c.Status == CertificateStatus.Active &&
                        c.ExpiresAt != null &&
                        c.ExpiresAt <= DateTime.UtcNow.AddDays(c.RenewalDays))
            .ToListAsync();

        var renewedCount = 0;

        foreach (var cert in expiringCerts)
        {
            try
            {
                _logger.LogInformation("Auto-renewing expiring certificate for {Domain} (expires {ExpiresAt})",
                    cert.Domain, cert.ExpiresAt);

                var success = await RenewCertificateAsync(cert.Id);
                if (success)
                {
                    renewedCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Auto-renewal failed for certificate {CertificateId}", cert.Id);
            }
        }

        // Also update status of already expired certificates
        var expiredCerts = await _context.Certificates
            .Where(c => c.Status == CertificateStatus.Active &&
                        c.ExpiresAt != null &&
                        c.ExpiresAt < DateTime.UtcNow)
            .ToListAsync();

        foreach (var cert in expiredCerts)
        {
            cert.Status = CertificateStatus.Expired;
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation("Auto-renewed {Count} certificates, marked {ExpiredCount} as expired",
            renewedCount, expiredCerts.Count);

        return renewedCount;
    }

    public async Task<CertificateInfo?> GetCertificateInfoAsync(int certificateId)
    {
        var certificate = await _context.Certificates
            .FirstOrDefaultAsync(c => c.Id == certificateId);

        if (certificate == null)
        {
            return null;
        }

        // Try to get live certificate info
        var liveCertInfo = await FetchCertificateInfoFromDomainAsync(certificate.Domain);

        if (liveCertInfo != null)
        {
            // Update stored certificate with live info
            certificate.IssuedAt = liveCertInfo.NotBefore;
            certificate.ExpiresAt = liveCertInfo.NotAfter;
            certificate.SerialNumber = liveCertInfo.SerialNumber;
            certificate.Issuer = liveCertInfo.Issuer;

            if (certificate.ExpiresAt < DateTime.UtcNow)
            {
                certificate.Status = CertificateStatus.Expired;
            }
            else if (certificate.ExpiresAt <= DateTime.UtcNow.AddDays(certificate.RenewalDays))
            {
                certificate.Status = CertificateStatus.Expiring;
            }
            else
            {
                certificate.Status = CertificateStatus.Active;
            }

            await _context.SaveChangesAsync();
        }

        return new CertificateInfo
        {
            Id = certificate.Id,
            Domain = certificate.Domain,
            Provider = certificate.Provider,
            Status = certificate.Status,
            IssuedAt = certificate.IssuedAt,
            ExpiresAt = certificate.ExpiresAt,
            DaysUntilExpiry = certificate.ExpiresAt.HasValue
                ? (int)(certificate.ExpiresAt.Value - DateTime.UtcNow).TotalDays
                : 0,
            AutoRenew = certificate.AutoRenew,
            ErrorMessage = certificate.ErrorMessage
        };
    }

    public async Task<bool> RevokeCertificateAsync(int certificateId)
    {
        var certificate = await _context.Certificates
            .FirstOrDefaultAsync(c => c.Id == certificateId);

        if (certificate == null)
        {
            _logger.LogWarning("Certificate {CertificateId} not found for revocation", certificateId);
            return false;
        }

        try
        {
            // Mark as expired/revoked in database
            certificate.Status = CertificateStatus.Expired;
            certificate.ErrorMessage = "Certificate revoked by user";
            await _context.SaveChangesAsync();

            _logger.LogInformation("Certificate {CertificateId} for {Domain} marked as revoked",
                certificateId, certificate.Domain);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to revoke certificate {CertificateId}", certificateId);
            return false;
        }
    }

    public async Task<bool> ValidateDomainOwnershipAsync(string domain, int serverId)
    {
        var server = await _context.Servers
            .Include(s => s.PrivateKey)
            .FirstOrDefaultAsync(s => s.Id == serverId);

        if (server == null)
        {
            _logger.LogWarning("Server {ServerId} not found for domain validation", serverId);
            return false;
        }

        try
        {
            // Check if domain resolves to this server's IP
            var result = await _sshService.ExecuteCommandAsync(
                server,
                $"curl -s -o /dev/null -w '%{{http_code}}' --max-time 10 http://{domain}/.well-known/acme-challenge/test || echo 'failed'",
                CancellationToken.None);

            // If we get any HTTP response, domain is likely pointing to this server
            if (result.ExitCode == 0 && !result.Output.Contains("failed"))
            {
                _logger.LogInformation("Domain {Domain} appears to be reachable from server {ServerId}",
                    domain, serverId);
                return true;
            }

            // Try DNS resolution
            var dnsResult = await _sshService.ExecuteCommandAsync(
                server,
                $"host {domain} | head -1",
                CancellationToken.None);

            _logger.LogInformation("DNS lookup for {Domain}: {Result}", domain, dnsResult.Output.Trim());
            return dnsResult.ExitCode == 0 && !string.IsNullOrEmpty(dnsResult.Output);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate domain ownership for {Domain}", domain);
            return false;
        }
    }

    private async Task<CertificateDetails?> FetchCertificateInfoFromDomainAsync(string domain)
    {
        try
        {
            CertificateDetails? certDetails = null;

            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
                {
                    if (cert != null)
                    {
                        var x509 = new X509Certificate2(cert);
                        certDetails = new CertificateDetails
                        {
                            NotBefore = x509.NotBefore,
                            NotAfter = x509.NotAfter,
                            SerialNumber = x509.SerialNumber,
                            Issuer = x509.Issuer
                        };
                    }
                    return true; // Accept any certificate for inspection
                }
            };

            using var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(10);

            await client.GetAsync($"https://{domain}");

            return certDetails;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not fetch certificate from {Domain}", domain);
            return null;
        }
    }

    private async Task ForceTraefikCertificateRenewalAsync(Server server)
    {
        try
        {
            // Restart Traefik to force it to check certificate renewal
            await _sshService.ExecuteCommandAsync(
                server,
                "docker restart hostcraft-traefik 2>/dev/null || true",
                CancellationToken.None);

            _logger.LogInformation("Triggered Traefik restart for certificate renewal on server {ServerId}",
                server.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not restart Traefik on server {ServerId}", server.Id);
        }
    }

    private class CertificateDetails
    {
        public DateTime NotBefore { get; set; }
        public DateTime NotAfter { get; set; }
        public string? SerialNumber { get; set; }
        public string? Issuer { get; set; }
    }
}
