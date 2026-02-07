using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using HostCraft.Core.Enums;
using HostCraft.Core.Interfaces;
using HostCraft.Core.Interfaces.Repositories;

namespace HostCraft.Infrastructure.Services;

/// <summary>
/// Service for handling Git OAuth operations.
/// </summary>
public class GitOAuthService : IGitOAuthService
{
    private readonly ISystemSettingsRepository _systemSettingsRepository;
    private readonly ILogger<GitOAuthService> _logger;

    public GitOAuthService(
        ISystemSettingsRepository systemSettingsRepository,
        ILogger<GitOAuthService> logger)
    {
        _systemSettingsRepository = systemSettingsRepository;
        _logger = logger;
    }

    /// <summary>
    /// Gets the public callback URL for OAuth, using SystemSettings API domain or forwarded headers.
    /// </summary>
    public async Task<string> GetPublicCallbackUrlAsync(HttpRequest request)
    {
        // Use AsNoTracking + FirstOrDefault with explicit Id filter for clean read
        // This avoids any EF caching issues
        var settings = await _systemSettingsRepository.GetAsync();

        _logger.LogInformation(
            "GetPublicCallbackUrl: Settings found: {Found}, ApiDomain: '{ApiDomain}', WebDomain: '{WebDomain}'",
            settings != null,
            settings?.HostCraftApiDomain ?? "(null)",
            settings?.HostCraftDomain ?? "(null)");

        // First priority: Use explicitly configured API domain
        if (settings != null && !string.IsNullOrEmpty(settings.HostCraftApiDomain))
        {
            var apiDomain = settings.HostCraftApiDomain.TrimEnd('/');
            var scheme = apiDomain.Contains("localhost") ? "http" : "https";

            string callbackUrl;
            if (apiDomain.StartsWith("http://") || apiDomain.StartsWith("https://"))
            {
                callbackUrl = $"{apiDomain}/api/gitproviders/callback";
            }
            else
            {
                callbackUrl = $"{scheme}://{apiDomain}/api/gitproviders/callback";
            }

            _logger.LogInformation("Using configured API domain for callback: {CallbackUrl}", callbackUrl);
            return callbackUrl;
        }

        // Second priority: Derive API domain from Web domain (assume same host, port 5100)
        if (settings != null && !string.IsNullOrEmpty(settings.HostCraftDomain))
        {
            var webDomain = settings.HostCraftDomain.TrimEnd('/');
            var scheme = webDomain.Contains("localhost") ? "http" : "https";

            // Remove any existing scheme
            if (webDomain.StartsWith("http://"))
            {
                webDomain = webDomain.Substring(7);
            }
            else if (webDomain.StartsWith("https://"))
            {
                webDomain = webDomain.Substring(8);
                scheme = "https";
            }

            // Replace port 5000 with 5100, or add port 5100 if no port specified
            if (webDomain.Contains(":5000"))
            {
                webDomain = webDomain.Replace(":5000", ":5100");
            }
            else if (!webDomain.Contains(":"))
            {
                // If using a domain without port (e.g., hostcraft.example.com),
                // assume API is at same domain (routed via reverse proxy)
                // Don't add port - let reverse proxy handle routing
            }

            var callbackUrl = $"{scheme}://{webDomain}/api/gitproviders/callback";
            _logger.LogInformation("Using derived callback URL from web domain: {CallbackUrl}", callbackUrl);
            return callbackUrl;
        }

        // Fall back to forwarded headers (X-Forwarded-Host, X-Forwarded-Proto)
        var forwardedHost = request.Headers["X-Forwarded-Host"].FirstOrDefault();
        var forwardedProto = request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? "https";

        _logger.LogInformation(
            "Checking forwarded headers: X-Forwarded-Host: {Host}, X-Forwarded-Proto: {Proto}",
            forwardedHost ?? "(null)",
            forwardedProto);

        if (!string.IsNullOrEmpty(forwardedHost))
        {
            var callbackUrl = $"{forwardedProto}://{forwardedHost}/api/gitproviders/callback";
            _logger.LogInformation("Using forwarded headers for callback: {CallbackUrl}", callbackUrl);
            return callbackUrl;
        }

        // Last resort: use request info (may not work behind reverse proxy)
        _logger.LogWarning(
            "No HostCraft API domain configured and no forwarded headers - OAuth callback URL may be incorrect. " +
            "Request.Scheme: {Scheme}, Request.Host: {Host}. " +
            "Configure HostCraft API Domain in Settings for OAuth to work correctly.",
            request.Scheme,
            request.Host);
        return $"{request.Scheme}://{request.Host}/api/gitproviders/callback";
    }

    /// <summary>
    /// Parses OAuth state parameter to extract provider type and optional API URL.
    /// </summary>
    public (GitProviderType Type, string? ApiUrl) ParseState(string state)
    {
        // State format: "github" or "gitlab:https://gitlab.example.com"
        var parts = state.Split(':', 2);
        var type = Enum.Parse<GitProviderType>(parts[0], ignoreCase: true);
        var apiUrl = parts.Length > 1 ? parts[1] : null;
        return (type, apiUrl);
    }
}
