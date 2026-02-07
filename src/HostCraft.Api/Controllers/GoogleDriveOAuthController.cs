using Microsoft.AspNetCore.Mvc;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.Util.Store;
using System.Collections.Concurrent;
using HostCraft.Core.Interfaces;

namespace HostCraft.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GoogleDriveOAuthController : ControllerBase
{
    private readonly ILogger<GoogleDriveOAuthController> _logger;
    private readonly IConfiguration _configuration;
    private readonly ISystemSettingsService _systemSettingsService;

    // In-memory storage for OAuth state (in production, use Redis or database)
    private static readonly ConcurrentDictionary<string, OAuthState> _oauthStates = new();

    public GoogleDriveOAuthController(
        ILogger<GoogleDriveOAuthController> logger,
        IConfiguration configuration,
        ISystemSettingsService systemSettingsService)
    {
        _logger = logger;
        _configuration = configuration;
        _systemSettingsService = systemSettingsService;
    }

    /// <summary>
    /// Initiates Google Drive OAuth2 flow and returns authorization URL
    /// </summary>
    [HttpPost("initiate")]
    public async Task<IActionResult> InitiateOAuth([FromBody] InitiateOAuthRequest request)
    {
        try
        {
            // Generate random state for CSRF protection
            var state = Guid.NewGuid().ToString("N");

            // Store state with timestamp for validation
            _oauthStates[state] = new OAuthState
            {
                CreatedAt = DateTime.UtcNow,
                ClientId = request.ClientId,
                ClientSecret = request.ClientSecret
            };

            // Clean up expired states (older than 10 minutes)
            CleanupExpiredStates();

            // Get redirect URI from system settings (uses HostCraft API Domain)
            var redirectUri = await GetRedirectUriAsync();

            // Build authorization URL
            var authorizationUrl = new GoogleAuthorizationCodeFlow(
                new GoogleAuthorizationCodeFlow.Initializer
                {
                    ClientSecrets = new ClientSecrets
                    {
                        ClientId = request.ClientId,
                        ClientSecret = request.ClientSecret
                    },
                    Scopes = new[] { DriveService.Scope.DriveFile }
                }).CreateAuthorizationCodeRequest(redirectUri).Build();

            // Add state parameter for CSRF protection
            var urlWithState = $"{authorizationUrl}&state={state}&access_type=offline&prompt=consent";

            return Ok(new
            {
                AuthorizationUrl = urlWithState,
                State = state
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initiating Google Drive OAuth");
            return StatusCode(500, new { Error = "Failed to initiate OAuth flow", Details = ex.Message });
        }
    }

    /// <summary>
    /// Handles OAuth2 callback from Google and exchanges code for tokens
    /// </summary>
    [HttpGet("callback")]
    public async Task<IActionResult> HandleCallback([FromQuery] string code, [FromQuery] string state, [FromQuery] string? error)
    {
        try
        {
            // Handle error from Google
            if (!string.IsNullOrEmpty(error))
            {
                _logger.LogWarning("Google OAuth error: {Error}", error);
                return Redirect($"/backups?oauth_error={Uri.EscapeDataString(error)}");
            }

            // Validate state
            if (string.IsNullOrEmpty(state) || !_oauthStates.TryRemove(state, out var oauthState))
            {
                _logger.LogWarning("Invalid OAuth state: {State}", state);
                return BadRequest(new { Error = "Invalid or expired OAuth state" });
            }

            // Exchange code for tokens (use same redirect URI as initiate)
            var redirectUri = await GetRedirectUriAsync();

            var flow = new GoogleAuthorizationCodeFlow(
                new GoogleAuthorizationCodeFlow.Initializer
                {
                    ClientSecrets = new ClientSecrets
                    {
                        ClientId = oauthState.ClientId,
                        ClientSecret = oauthState.ClientSecret
                    },
                    Scopes = new[] { DriveService.Scope.DriveFile }
                });

            var token = await flow.ExchangeCodeForTokenAsync(
                "user",
                code,
                redirectUri,
                CancellationToken.None);

            // Return HTML that posts message to parent window (for popup flow)
            var html = $@"
<!DOCTYPE html>
<html>
<head>
    <title>Google Drive Connected</title>
    <style>
        body {{
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Oxygen, Ubuntu, Cantarell, sans-serif;
            display: flex;
            justify-content: center;
            align-items: center;
            height: 100vh;
            margin: 0;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: white;
        }}
        .container {{
            text-align: center;
            padding: 2rem;
            background: rgba(255, 255, 255, 0.1);
            border-radius: 1rem;
            backdrop-filter: blur(10px);
        }}
        .checkmark {{
            font-size: 4rem;
            margin-bottom: 1rem;
        }}
        h1 {{
            margin: 0 0 0.5rem 0;
            font-size: 1.5rem;
        }}
        p {{
            margin: 0;
            opacity: 0.9;
        }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='checkmark'>✓</div>
        <h1>Connected to Google Drive!</h1>
        <p>This window will close automatically...</p>
    </div>
    <script>
        // Send tokens to parent window
        if (window.opener) {{
            window.opener.postMessage({{
                type: 'google_oauth_success',
                tokens: {{
                    accessToken: '{token.AccessToken}',
                    refreshToken: '{token.RefreshToken}',
                    expiresInSeconds: {token.ExpiresInSeconds ?? 3600},
                    tokenType: '{token.TokenType}'
                }}
            }}, '*');

            // Close popup after 2 seconds
            setTimeout(() => window.close(), 2000);
        }} else {{
            // If not in popup, redirect back to backups page
            window.location.href = '/backups?oauth_success=true';
        }}
    </script>
</body>
</html>";

            return Content(html, "text/html");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling Google OAuth callback");
            return Redirect($"/backups?oauth_error={Uri.EscapeDataString("Failed to exchange authorization code")}");
        }
    }

    /// <summary>
    /// Tests Google Drive connection with provided credentials
    /// </summary>
    [HttpPost("test-connection")]
    public async Task<IActionResult> TestConnection([FromBody] TestGoogleDriveRequest request)
    {
        try
        {
            var credential = GoogleCredential.FromAccessToken(request.AccessToken);

            var service = new DriveService(new Google.Apis.Services.BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "HostCraft"
            });

            // Try to get user info (about request)
            var aboutRequest = service.About.Get();
            aboutRequest.Fields = "user";
            var about = await aboutRequest.ExecuteAsync();

            return Ok(new
            {
                Success = true,
                Message = $"Successfully connected to Google Drive as {about.User.EmailAddress}",
                UserEmail = about.User.EmailAddress
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing Google Drive connection");
            return Ok(new
            {
                Success = false,
                Message = $"Failed to connect: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Gets the OAuth redirect URI from system settings (HostCraft API Domain).
    /// Falls back to current request host if not configured.
    /// </summary>
    private async Task<string> GetRedirectUriAsync()
    {
        try
        {
            var settings = await _systemSettingsService.GetSettingsAsync();

            if (settings != null)
            {
                // Prefer HostCraftApiDomain, fallback to HostCraftDomain
                var domain = settings.HostCraftApiDomain ?? settings.HostCraftDomain;

                if (!string.IsNullOrEmpty(domain))
                {
                    // Use HTTPS if enabled in settings
                    var scheme = settings.HostCraftEnableHttps ? "https" : "http";
                    return $"{scheme}://{domain}/api/GoogleDriveOAuth/callback";
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get system settings for OAuth redirect URI, falling back to request host");
        }

        // Fallback to current request host
        return $"{Request.Scheme}://{Request.Host}/api/GoogleDriveOAuth/callback";
    }

    private void CleanupExpiredStates()
    {
        var expiredStates = _oauthStates
            .Where(kvp => DateTime.UtcNow - kvp.Value.CreatedAt > TimeSpan.FromMinutes(10))
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var state in expiredStates)
        {
            _oauthStates.TryRemove(state, out _);
        }
    }

    private class OAuthState
    {
        public DateTime CreatedAt { get; set; }
        public required string ClientId { get; set; }
        public required string ClientSecret { get; set; }
    }
}

public class InitiateOAuthRequest
{
    public required string ClientId { get; set; }
    public required string ClientSecret { get; set; }
}

public class TestGoogleDriveRequest
{
    public required string AccessToken { get; set; }
}
