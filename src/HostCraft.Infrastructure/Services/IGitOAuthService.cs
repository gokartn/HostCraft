using Microsoft.AspNetCore.Http;
using HostCraft.Core.Enums;

namespace HostCraft.Infrastructure.Services;

/// <summary>
/// Service for handling Git OAuth operations.
/// </summary>
public interface IGitOAuthService
{
    /// <summary>
    /// Gets the public callback URL for OAuth, using SystemSettings API domain or forwarded headers.
    /// </summary>
    Task<string> GetPublicCallbackUrlAsync(HttpRequest request);

    /// <summary>
    /// Parses OAuth state parameter to extract provider type and optional API URL.
    /// </summary>
    (GitProviderType Type, string? ApiUrl) ParseState(string state);
}
