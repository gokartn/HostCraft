using HostCraft.Core.Interfaces;
using HostCraft.Core.Models;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;

namespace HostCraft.Infrastructure.Services;

/// <summary>
/// Validates that a domain's DNS configuration resolves to the expected server IP.
/// </summary>
public class DnsValidationService : IDnsValidationService
{
    private readonly ILogger<DnsValidationService> _logger;

    public DnsValidationService(ILogger<DnsValidationService> logger)
    {
        _logger = logger;
    }

    public async Task<DomainDnsValidationResult> ValidateAsync(
        string domainHost,
        string expectedServerHost,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Localhost/private addresses cannot be validated via DNS
            if (IsLocalhostOrPrivate(expectedServerHost))
            {
                return new DomainDnsValidationResult
                {
                    IsValid = false,
                    Error = $"Cannot validate DNS for {expectedServerHost}. Deploy to a server with a public IP address.",
                    ExpectedIp = expectedServerHost,
                    ActualIp = null
                };
            }

            // Perform DNS lookup
            var hostEntry = await Dns.GetHostEntryAsync(domainHost, cancellationToken);
            var resolvedIps = hostEntry.AddressList
                .Select(ip => ip.ToString())
                .ToList();

            // Check if expected server IP matches any resolved IP
            var isValid = resolvedIps.Contains(expectedServerHost);

            return new DomainDnsValidationResult
            {
                IsValid = isValid,
                ExpectedIp = expectedServerHost,
                ActualIp = resolvedIps.FirstOrDefault(),
                AllResolvedIps = resolvedIps,
                Error = isValid 
                    ? null 
                    : $"DNS points to {string.Join(", ", resolvedIps)} but expected {expectedServerHost}"
            };
        }
        catch (SocketException ex)
        {
            _logger.LogWarning(ex, "DNS lookup failed for domain {Domain}", domainHost);
            return new DomainDnsValidationResult
            {
                IsValid = false,
                Error = $"DNS lookup failed: {ex.Message}",
                ExpectedIp = expectedServerHost
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error validating DNS for domain {Domain}", domainHost);
            return new DomainDnsValidationResult
            {
                IsValid = false,
                Error = $"Validation error: {ex.Message}",
                ExpectedIp = expectedServerHost
            };
        }
    }

    private static bool IsLocalhostOrPrivate(string host)
    {
        if (host == "localhost" || host == "127.0.0.1" || host == "::1")
            return true;

        // Check for private IP ranges
        if (IPAddress.TryParse(host, out var ipAddress))
        {
            var bytes = ipAddress.GetAddressBytes();
            
            // IPv4 private ranges
            if (bytes.Length == 4)
            {
                // 10.0.0.0/8
                if (bytes[0] == 10)
                    return true;
                
                // 172.16.0.0/12
                if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                    return true;
                
                // 192.168.0.0/16
                if (bytes[0] == 192 && bytes[1] == 168)
                    return true;
            }
        }

        return false;
    }
}
