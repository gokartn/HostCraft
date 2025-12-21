using System.Net.Http.Headers;
using HostCraft.Web.Services;

namespace HostCraft.Web.Handlers;

/// <summary>
/// HTTP message handler that automatically adds JWT authentication to API requests.
/// </summary>
public class AuthenticationHandler : DelegatingHandler
{
    private readonly IWebAuthService _authService;
    private readonly ILogger<AuthenticationHandler> _logger;

    public AuthenticationHandler(IWebAuthService authService, ILogger<AuthenticationHandler> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            // Get the current token
            var token = await _authService.GetTokenAsync();

            if (!string.IsNullOrEmpty(token))
            {
                // Add authorization header if we have a token
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                _logger.LogDebug("Added JWT token to request: {Method} {Url}", request.Method, request.RequestUri);
            }
            else
            {
                _logger.LogDebug("No JWT token available for request: {Method} {Url}", request.Method, request.RequestUri);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding authentication to request");
        }

        // Send the request
        var response = await base.SendAsync(request, cancellationToken);

        // If we get a 401 Unauthorized, try to refresh the token and retry once
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning("Received 401 Unauthorized, attempting token refresh");

            try
            {
                var refreshResult = await _authService.RefreshTokenAsync();
                if (refreshResult.Success && !string.IsNullOrEmpty(refreshResult.Token))
                {
                    // Retry the request with the new token
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshResult.Token);
                    _logger.LogInformation("Retrying request with refreshed token");

                    // Create a new request to avoid issues with the original request
                    var retryRequest = new HttpRequestMessage(request.Method, request.RequestUri)
                    {
                        Content = request.Content,
                        Version = request.Version
                    };

                    // Copy headers
                    foreach (var header in request.Headers)
                    {
                        retryRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }

                    response = await base.SendAsync(retryRequest, cancellationToken);
                }
                else
                {
                    _logger.LogWarning("Token refresh failed, request will fail with 401");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during token refresh retry");
            }
        }

        return response;
    }
}