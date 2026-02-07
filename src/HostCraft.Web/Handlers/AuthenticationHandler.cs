using System.Net.Http.Headers;
using HostCraft.Web.Services;

namespace HostCraft.Web.Handlers;

/// <summary>
/// HTTP message handler that automatically adds JWT authentication to API requests.
/// Uses ITokenStore (singleton) directly to avoid DI scope issues with IWebAuthService.
/// </summary>
public class AuthenticationHandler : DelegatingHandler
{
    private readonly ITokenStore _tokenStore;
    private readonly ILogger<AuthenticationHandler> _logger;

    public AuthenticationHandler(ITokenStore tokenStore, ILogger<AuthenticationHandler> logger)
    {
        _tokenStore = tokenStore;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            // Don't add authentication to anonymous endpoints
            var isAnonymousEndpoint = request.RequestUri?.PathAndQuery.Contains("api/auth/setup-required") == true ||
                                     request.RequestUri?.PathAndQuery.Contains("api/auth/setup") == true ||
                                     request.RequestUri?.PathAndQuery.Contains("api/auth/login") == true ||
                                     request.RequestUri?.PathAndQuery.Contains("api/auth/refresh") == true ||
                                     request.RequestUri?.PathAndQuery.Contains("api/auth/2fa/verify") == true;

            if (!isAnonymousEndpoint)
            {
                // Get the current token from singleton store (works across all DI scopes)
                var token = _tokenStore.GetAnyValidToken();

                if (!string.IsNullOrEmpty(token))
                {
                    // Add authorization header if we have a token
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    _logger.LogInformation("AuthHandler: Added JWT token to request: {Method} {Url}", request.Method, request.RequestUri);
                }
                else
                {
                    _logger.LogWarning("AuthHandler: NO TOKEN for request: {Method} {Url}", request.Method, request.RequestUri);
                }
            }
            else
            {
                _logger.LogDebug("Skipping authentication for anonymous endpoint: {Method} {Url}", request.Method, request.RequestUri);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding authentication to request");
        }

        // Send the request
        var response = await base.SendAsync(request, cancellationToken);

        // Note: Token refresh is handled by the calling code (AuthService) when it detects a 401.
        // We don't do automatic refresh here to avoid circular dependencies and scope issues.

        return response;
    }
}