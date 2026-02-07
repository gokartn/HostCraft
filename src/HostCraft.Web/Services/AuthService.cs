using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using HostCraft.Web.Models;

namespace HostCraft.Web.Services;

/// <summary>
/// Authentication service for the HostCraft web application.
/// Handles login, logout, and JWT token management.
/// </summary>
public class AuthService : IWebAuthService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AuthenticationStateProvider _authStateProvider;
    private readonly IJSRuntime _jsRuntime;
    private readonly ILogger<AuthService> _logger;
    private readonly ITokenStore _tokenStore;

    // Session identifier for this circuit
    private string _sessionId = Guid.NewGuid().ToString();

    // Local cache for faster access within the same circuit
    private string? _cachedToken;
    private string? _cachedRefreshToken;

    public AuthService(
        IHttpClientFactory httpClientFactory,
        AuthenticationStateProvider authStateProvider,
        IJSRuntime jsRuntime,
        ILogger<AuthService> logger,
        ITokenStore tokenStore)
    {
        _httpClientFactory = httpClientFactory;
        _authStateProvider = authStateProvider;
        _jsRuntime = jsRuntime;
        _logger = logger;
        _tokenStore = tokenStore;
    }

    private HttpClient CreateHttpClient()
    {
        return _httpClientFactory.CreateClient("HostCraftAPI");
    }

    /// <summary>
    /// Attempts to log in a user with email and password.
    /// </summary>
    public async Task<AuthResult> LoginAsync(string email, string password)
    {
        try
        {
            var loginRequest = new LoginRequest
            {
                Email = email,
                Password = password
            };

            var response = await CreateHttpClient().PostAsJsonAsync("api/auth/login", loginRequest);

            if (response.IsSuccessStatusCode)
            {
                var loginResponse = await response.Content.ReadFromJsonAsync<LoginResponse>();
                // API returns 200 OK on success - check required fields are present
                if (loginResponse != null && !string.IsNullOrEmpty(loginResponse.Token) && !string.IsNullOrEmpty(loginResponse.RefreshToken) && loginResponse.User != null)
                {
                    // Cache tokens locally for this circuit
                    _cachedToken = loginResponse.Token;
                    _cachedRefreshToken = loginResponse.RefreshToken;

                    // Store in singleton token store (for SignalR hubs and cross-scope access)
                    _tokenStore.SetTokens(_sessionId, loginResponse.Token, loginResponse.RefreshToken);

                    // Store tokens in browser local storage for persistence across page refreshes
                    try
                    {
                        await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "authToken", loginResponse.Token);
                        await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "refreshToken", loginResponse.RefreshToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to store tokens in localStorage (pre-render or SSR context)");
                    }

                    // Notify authentication state provider
                    if (_authStateProvider is HostCraftAuthenticationStateProvider hostCraftAuthProvider)
                    {
                        await hostCraftAuthProvider.MarkUserAsAuthenticated(loginResponse.User);
                    }

                    _logger.LogInformation("User {Email} logged in successfully", email);
                    return AuthResult.Succeeded(loginResponse.Token, loginResponse.RefreshToken, loginResponse.ExpiresAt, loginResponse.User);
                }
                else
                {
                    var error = loginResponse?.Error ?? "Login failed";
                    _logger.LogWarning("Login failed for user {Email}: {Error}", email, error);
                    return AuthResult.Failed(error);
                }
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Login request failed for user {Email}: {StatusCode} - {Error}", email, response.StatusCode, errorContent);

                var extracted = ExtractApiErrorMessage(errorContent) ?? "Invalid email or password";
                return AuthResult.Failed(extracted);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during login for user {Email}", email);
            return AuthResult.Failed("An error occurred during login");
        }
    }

    /// <summary>
    /// Logs out the current user.
    /// </summary>
    public async Task LogoutAsync()
    {
        try
        {
            // Clear local token cache
            _cachedToken = null;
            _cachedRefreshToken = null;

            // Clear from singleton token store
            _tokenStore.ClearTokens(_sessionId);

            // Clear tokens from local storage
            try
            {
                await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", "authToken");
                await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", "refreshToken");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clear localStorage (SSR context)");
            }

            // Notify authentication state provider
            if (_authStateProvider is HostCraftAuthenticationStateProvider hostCraftAuthProvider)
            {
                await hostCraftAuthProvider.MarkUserAsLoggedOut();
            }

            _logger.LogInformation("User logged out successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during logout");
        }
    }

    /// <summary>
    /// Gets the current authentication token.
    /// </summary>
    public async Task<string?> GetTokenAsync()
    {
        // First check local cache (fastest path)
        if (!string.IsNullOrEmpty(_cachedToken))
        {
            return _cachedToken;
        }

        // Check singleton token store (works across scopes, for SignalR hubs)
        var (storedToken, _) = _tokenStore.GetTokens(_sessionId);
        if (!string.IsNullOrEmpty(storedToken))
        {
            _cachedToken = storedToken;
            return storedToken;
        }

        // Fall back to any valid token in the store (for cross-scope access)
        var anyToken = _tokenStore.GetAnyValidToken();
        if (!string.IsNullOrEmpty(anyToken))
        {
            _cachedToken = anyToken;
            return anyToken;
        }

        // Finally, try localStorage for page refresh scenarios
        try
        {
            string? token = null;

            // Check if JS interop is available (not during pre-rendering)
            if (_jsRuntime is IJSInProcessRuntime inProcessRuntime)
            {
                token = inProcessRuntime.Invoke<string>("localStorage.getItem", "authToken");
            }
            else
            {
                token = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", "authToken");
            }

            // Cache the token if found
            if (!string.IsNullOrEmpty(token))
            {
                _cachedToken = token;
                // Also store in singleton for other scopes
                var refreshToken = await GetRefreshTokenFromStorageAsync();
                if (!string.IsNullOrEmpty(refreshToken))
                {
                    _tokenStore.SetTokens(_sessionId, token, refreshToken);
                }
            }

            return token;
        }
        catch (InvalidOperationException)
        {
            // JS interop not available during pre-rendering
            _logger.LogDebug("JS interop not available yet (pre-rendering)");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error getting token from localStorage");
            return null;
        }
    }

    private async Task<string?> GetRefreshTokenFromStorageAsync()
    {
        try
        {
            return await _jsRuntime.InvokeAsync<string>("localStorage.getItem", "refreshToken");
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the current refresh token.
    /// </summary>
    public async Task<string?> GetRefreshTokenAsync()
    {
        // First check local cache
        if (!string.IsNullOrEmpty(_cachedRefreshToken))
        {
            return _cachedRefreshToken;
        }

        // Check singleton token store
        var (_, storedRefreshToken) = _tokenStore.GetTokens(_sessionId);
        if (!string.IsNullOrEmpty(storedRefreshToken))
        {
            _cachedRefreshToken = storedRefreshToken;
            return storedRefreshToken;
        }

        // Try localStorage
        try
        {
            var token = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", "refreshToken");
            if (!string.IsNullOrEmpty(token))
            {
                _cachedRefreshToken = token;
            }
            return token;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Refreshes the authentication token.
    /// </summary>
    public async Task<AuthResult> RefreshTokenAsync()
    {
        try
        {
            var refreshToken = await GetRefreshTokenAsync();
            if (string.IsNullOrEmpty(refreshToken))
            {
                return AuthResult.Failed("No refresh token available");
            }

            var refreshRequest = new RefreshTokenRequest
            {
                RefreshToken = refreshToken
            };

            var response = await CreateHttpClient().PostAsJsonAsync("api/auth/refresh", refreshRequest);

            if (response.IsSuccessStatusCode)
            {
                var loginResponse = await response.Content.ReadFromJsonAsync<LoginResponse>();
                // API returns 200 OK on success - check required fields are present
                if (loginResponse != null && !string.IsNullOrEmpty(loginResponse.Token) && !string.IsNullOrEmpty(loginResponse.RefreshToken) && loginResponse.User != null)
                {
                    // Update local token cache
                    _cachedToken = loginResponse.Token;
                    _cachedRefreshToken = loginResponse.RefreshToken;

                    // Update singleton token store
                    _tokenStore.SetTokens(_sessionId, loginResponse.Token, loginResponse.RefreshToken);

                    // Update tokens in local storage
                    try
                    {
                        await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "authToken", loginResponse.Token);
                        await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "refreshToken", loginResponse.RefreshToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to update localStorage (SSR context)");
                    }

                    _logger.LogInformation("Token refreshed successfully");
                    return AuthResult.Succeeded(loginResponse.Token, loginResponse.RefreshToken, loginResponse.ExpiresAt, loginResponse.User);
                }
                else
                {
                    var error = loginResponse?.Error ?? "Token refresh failed";
                    _logger.LogWarning("Token refresh failed: {Error}", error);
                    return AuthResult.Failed(error);
                }
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Token refresh request failed: {StatusCode} - {Error}", response.StatusCode, errorContent);
                var extracted = ExtractApiErrorMessage(errorContent) ?? $"Token refresh failed: {response.StatusCode}";
                return AuthResult.Failed(extracted);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during token refresh");
            return AuthResult.Failed("An error occurred during token refresh");
        }
    }

    /// <summary>
    /// Checks if the user is currently authenticated.
    /// </summary>
    public async Task<bool> IsAuthenticatedAsync()
    {
        var token = await GetTokenAsync();
        return !string.IsNullOrEmpty(token);
    }

    /// <summary>
    /// Initializes the authentication state on app startup.
    /// Restores tokens from localStorage and validates them with the API.
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            // If we already have a cached token AND the auth state is already authenticated, skip
            if (!string.IsNullOrEmpty(_cachedToken) &&
                _authStateProvider is HostCraftAuthenticationStateProvider provider &&
                provider.IsAuthenticated())
            {
                _logger.LogDebug("Auth already initialized with cached token and authenticated state");
                return;
            }

            _logger.LogInformation("Initializing authentication state...");

            // Try to load token from localStorage (needed after page refresh)
            string? token = null;
            string? refreshToken = null;

            try
            {
                token = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", "authToken");
                refreshToken = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", "refreshToken");

                _logger.LogInformation("Loaded tokens from localStorage: hasToken={HasToken}, hasRefresh={HasRefresh}",
                    !string.IsNullOrEmpty(token), !string.IsNullOrEmpty(refreshToken));
            }
            catch (InvalidOperationException)
            {
                _logger.LogDebug("JS interop not available (pre-rendering), skipping localStorage");
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load tokens from localStorage");
                return;
            }

            if (string.IsNullOrEmpty(token))
            {
                _logger.LogInformation("No token in localStorage, user is not authenticated");
                return;
            }

            // Store token in singleton TokenStore and local cache BEFORE making API call
            // This ensures the AuthenticationHandler can find it
            _cachedToken = token;
            _cachedRefreshToken = refreshToken;
            _tokenStore.SetTokens(_sessionId, token, refreshToken ?? string.Empty);

            _logger.LogInformation("Token restored from localStorage and stored in TokenStore");

            // Validate the token by fetching the current user from API
            try
            {
                var client = CreateHttpClient();
                var response = await client.GetAsync("api/auth/me");

                if (response.IsSuccessStatusCode)
                {
                    var user = await response.Content.ReadFromJsonAsync<UserDto>();
                    if (user != null && _authStateProvider is HostCraftAuthenticationStateProvider hostCraftAuthProvider)
                    {
                        await hostCraftAuthProvider.MarkUserAsAuthenticated(user);
                        _logger.LogInformation("User {Email} authenticated from stored token", user.Email);
                        return;
                    }
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    _logger.LogInformation("Stored token is invalid or expired, attempting refresh...");

                    // Try to refresh the token
                    if (!string.IsNullOrEmpty(refreshToken))
                    {
                        var refreshResult = await RefreshTokenAsync();
                        if (refreshResult.Success && refreshResult.User != null)
                        {
                            if (_authStateProvider is HostCraftAuthenticationStateProvider hostCraftAuthProvider)
                            {
                                await hostCraftAuthProvider.MarkUserAsAuthenticated(refreshResult.User);
                                _logger.LogInformation("User authenticated after token refresh");
                                return;
                            }
                        }
                    }

                    // Token refresh failed - clear everything
                    _logger.LogWarning("Token refresh failed, clearing stored tokens");
                    await ClearStoredTokensAsync();
                }
                else
                {
                    _logger.LogWarning("Failed to validate token: {StatusCode}", response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating stored token with API");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during authentication initialization");
        }
    }

    /// <summary>
    /// Clears all stored tokens (cache, token store, localStorage).
    /// </summary>
    private async Task ClearStoredTokensAsync()
    {
        _cachedToken = null;
        _cachedRefreshToken = null;
        _tokenStore.ClearTokens(_sessionId);

        try
        {
            await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", "authToken");
            await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", "refreshToken");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to clear localStorage (may be in SSR context)");
        }
    }

    private static string? ExtractApiErrorMessage(string errorContent)
    {
        if (string.IsNullOrWhiteSpace(errorContent))
        {
            return null;
        }

        try
        {
            var apiError = JsonSerializer.Deserialize<ApiError>(errorContent);
            if (apiError != null)
            {
                if (apiError.Errors != null && apiError.Errors.Values.SelectMany(v => v).FirstOrDefault() is { } firstError)
                {
                    return firstError;
                }

                if (!string.IsNullOrWhiteSpace(apiError.Detail))
                {
                    return apiError.Detail;
                }

                if (!string.IsNullOrWhiteSpace(apiError.Title))
                {
                    return apiError.Title;
                }

                if (!string.IsNullOrWhiteSpace(apiError.Code))
                {
                    return apiError.Code;
                }
            }

            using var doc = JsonDocument.Parse(errorContent);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var errorProp))
            {
                return errorProp.GetString();
            }
        }
        catch
        {
            // Ignore parsing errors; fall back to defaults
        }

        return null;
    }
}

/// <summary>
/// Interface for the authentication service.
/// </summary>
public interface IWebAuthService
{
    Task<AuthResult> LoginAsync(string email, string password);
    Task LogoutAsync();
    Task<string?> GetTokenAsync();
    Task<string?> GetRefreshTokenAsync();
    Task<AuthResult> RefreshTokenAsync();
    Task<bool> IsAuthenticatedAsync();
    Task InitializeAsync();
}