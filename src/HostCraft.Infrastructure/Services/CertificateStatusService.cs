using HostCraft.Core.Entities;
using HostCraft.Core.Interfaces;
using HostCraft.Core.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HostCraft.Infrastructure.Services;

/// <summary>
/// Service for checking certificate status from Traefik logs and Let's Encrypt
/// </summary>
public class CertificateStatusService : ICertificateStatusService
{
    private readonly ISshService _sshService;
    private readonly ILogger<CertificateStatusService> _logger;

    public CertificateStatusService(ISshService sshService, ILogger<CertificateStatusService> logger)
    {
        _sshService = sshService;
        _logger = logger;
    }

    public async Task<DomainCertificateStatus> GetCertificateStatusAsync(Server server, string domain)
    {
        try
        {
            // Get Traefik service logs to check certificate status
            var logs = await GetTraefikLogsAsync(server);
            return ParseCertificateStatusFromLogs(server, domain, logs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking certificate status for domain {Domain}", domain);
            return new DomainCertificateStatus
            {
                Domain = domain,
                Status = "unknown",
                ErrorMessage = ex.Message,
                LastChecked = DateTime.UtcNow
            };
        }
    }

    public async Task<List<DomainCertificateStatus>> GetAllCertificateStatusesAsync(Server server, List<string> domains)
    {
        try
        {
            var logs = await GetTraefikLogsAsync(server);
            return domains.Select(domain => ParseCertificateStatusFromLogs(server, domain, logs)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking certificate statuses");
            return domains.Select(domain => new DomainCertificateStatus
            {
                Domain = domain,
                Status = "unknown",
                ErrorMessage = ex.Message,
                LastChecked = DateTime.UtcNow
            }).ToList();
        }
    }

    private async Task<string> GetTraefikLogsAsync(Server server)
    {
        try
        {
            // Get Traefik service logs for the last hour
            var result = await _sshService.ExecuteCommandAsync(server, "docker service logs traefik_traefik --since 1h --tail 200 2>&1 | grep -E 'acme|certificate|tls|error.*hostcraft|error.*test'");
            return result.Output;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not get Traefik logs");
            return string.Empty;
        }
    }

    private DomainCertificateStatus ParseCertificateStatusFromLogs(Server server, string domain, string logs)
    {
        var status = new DomainCertificateStatus
        {
            Domain = domain,
            Status = "unknown",
            LastChecked = DateTime.UtcNow
        };

        if (string.IsNullOrEmpty(logs))
        {
            status.Status = "no_logs";
            status.ErrorMessage = "Unable to retrieve Traefik logs";
            return status;
        }

        var domainEscaped = Regex.Escape(domain);
        
        // Check for rate limiting
        var rateLimitPattern = $@"rateLimited.*{domainEscaped}.*retry after (\d{{4}}-\d{{2}}-\d{{2}} \d{{2}}:\d{{2}}:\d{{2}})";
        var rateLimitMatch = Regex.Match(logs, rateLimitPattern, RegexOptions.IgnoreCase);
        if (rateLimitMatch.Success)
        {
            status.Status = "rate_limited";
            status.ErrorMessage = $"Let's Encrypt rate limited. Retry after {rateLimitMatch.Groups[1].Value} UTC";
            if (DateTime.TryParse(rateLimitMatch.Groups[1].Value, out var retryTime))
            {
                status.RetryAfter = retryTime;
            }
            return status;
        }

        // Check for ACME challenge errors
        var acmeErrorPattern = $@"Unable to obtain ACME certificate for domains.*{domainEscaped}.*?error: (\d+).*?([^""]+)";
        var acmeErrorMatch = Regex.Match(logs, acmeErrorPattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (acmeErrorMatch.Success)
        {
            status.Status = "acme_error";
            status.ErrorMessage = $"ACME Error {acmeErrorMatch.Groups[1].Value}: {acmeErrorMatch.Groups[2].Value}";
            return status;
        }

        // Check for rule parsing errors (backslash escaping issue)
        var ruleErrorPattern = $@"error while parsing rule.*{domainEscaped}.*illegal character";
        if (Regex.IsMatch(logs, ruleErrorPattern, RegexOptions.IgnoreCase))
        {
            status.Status = "config_error";
            status.ErrorMessage = "Traefik rule parsing error (label escaping issue)";
            return status;
        }

        // Check for successful certificate issuance
        var successPattern = $@"certificate.*{domainEscaped}.*obtained|successfully.*{domainEscaped}";
        if (Regex.IsMatch(logs, successPattern, RegexOptions.IgnoreCase))
        {
            status.Status = "valid";
            return status;
        }

        // Note: Could add acme.json check here, but would require making this method async
        // For now, rely on log parsing for status determination

        // Default status
        status.Status = "pending";
        return status;
    }
}