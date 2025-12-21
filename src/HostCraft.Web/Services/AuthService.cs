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
    private readonly HttpClient _httpClient;
    private readonly AuthenticationStateProvider _authStateProvider;
    private readonly IJSRuntime _jsRuntime;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        HttpClient httpClient,
        AuthenticationStateProvider authStateProvider,
        IJSRuntime jsRuntime,
        ILogger<AuthService> logger)
    {
        _httpClient = httpClient;
        _authStateProvider = authStateProvider;
        _jsRuntime = jsRuntime;
        _logger = logger;
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

            var response = await _httpClient.PostAsJsonAsync("api/auth/login", loginRequest);

            if (response.IsSuccessStatusCode)
            {
                var authResponse = await response.Content.ReadFromJsonAsync<AuthResponse>();
                if (authResponse?.Success == true && !string.IsNullOrEmpty(authResponse.Token) && !string.IsNullOrEmpty(authResponse.RefreshToken) && authResponse.User != null)
                {
                    // Store tokens in local storage
                    await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "authToken", authResponse.Token);
                    await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "refreshToken", authResponse.RefreshToken);

                    // Notify authentication state provider
                    if (_authStateProvider is HostCraftAuthenticationStateProvider hostCraftAuthProvider)
                    {
                        await hostCraftAuthProvider.MarkUserAsAuthenticated(authResponse.User);
                    }

                    _logger.LogInformation("User {Email} logged in successfully", email);
                    return AuthResult.Succeeded(authResponse.Token, authResponse.RefreshToken, authResponse.ExpiresAt, authResponse.User);
                }
                else
                {
                    var error = authResponse?.Error ?? "Login failed";
                    _logger.LogWarning("Login failed for user {Email}: {Error}", email, error);
                    return AuthResult.Failed(error);
                }
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Login request failed for user {Email}: {StatusCode} - {Error}", email, response.StatusCode, errorContent);
                return AuthResult.Failed($"Login failed: {response.StatusCode}");
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
            // Clear tokens from local storage
            await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", "authToken");
            await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", "refreshToken");

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
        try
        {
            return await _jsRuntime.InvokeAsync<string>("localStorage.getItem", "authToken");
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

            var response = await _httpClient.PostAsJsonAsync("api/auth/refresh", refreshRequest);

            if (response.IsSuccessStatusCode)
            {
                var authResponse = await response.Content.ReadFromJsonAsync<AuthResponse>();
                if (authResponse?.Success == true && !string.IsNullOrEmpty(authResponse.Token) && !string.IsNullOrEmpty(authResponse.RefreshToken) && authResponse.User != null)
                {
                    // Update tokens in local storage
                    await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "authToken", authResponse.Token);
                    await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "refreshToken", authResponse.RefreshToken);

                    _logger.LogInformation("Token refreshed successfully");
                    return AuthResult.Succeeded(authResponse.Token, authResponse.RefreshToken, authResponse.ExpiresAt, authResponse.User);
                }
                else
                {
                    var error = authResponse?.Error ?? "Token refresh failed";
                    _logger.LogWarning("Token refresh failed: {Error}", error);
                    return AuthResult.Failed(error);
                }
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Token refresh request failed: {StatusCode} - {Error}", response.StatusCode, errorContent);
                return AuthResult.Failed($"Token refresh failed: {response.StatusCode}");
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
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            var token = await GetTokenAsync();
            if (!string.IsNullOrEmpty(token))
            {
                // Try to validate the token by making a request to get current user
                try
                {
                    var response = await _httpClient.GetAsync("api/auth/me");
                    if (response.IsSuccessStatusCode)
                    {
                        var user = await response.Content.ReadFromJsonAsync<User>();
                        if (_authStateProvider is HostCraftAuthenticationStateProvider hostCraftAuthProvider)
                        {
                            await hostCraftAuthProvider.MarkUserAsAuthenticated(user);
                        }
                    }
                    else
                    {
                        // Token is invalid, clear it
                        await LogoutAsync();
                    }
                }
                catch
                {
                    // Token validation failed, clear it
                    await LogoutAsync();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during authentication initialization");
        }
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