using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using HostCraft.Web.Models;

namespace HostCraft.Web.Services;

/// <summary>
/// Custom authentication state provider for HostCraft.
/// Manages the authentication state for Blazor components.
/// </summary>
public class HostCraftAuthenticationStateProvider : AuthenticationStateProvider
{
    private AuthenticationState _currentState = new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        return Task.FromResult(_currentState);
    }

    /// <summary>
    /// Marks the user as authenticated with the provided user information.
    /// </summary>
    public async Task MarkUserAsAuthenticated(User? user)
    {
        if (user == null)
        {
            await MarkUserAsLoggedOut();
            return;
        }

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Name, user.Name ?? user.Email),
            new Claim("IsAdmin", user.IsAdmin.ToString())
        }, "HostCraft");

        var principal = new ClaimsPrincipal(identity);
        _currentState = new AuthenticationState(principal);

        NotifyAuthenticationStateChanged(Task.FromResult(_currentState));
    }

    /// <summary>
    /// Marks the user as logged out.
    /// </summary>
    public async Task MarkUserAsLoggedOut()
    {
        var identity = new ClaimsIdentity();
        var principal = new ClaimsPrincipal(identity);
        _currentState = new AuthenticationState(principal);

        NotifyAuthenticationStateChanged(Task.FromResult(_currentState));
    }

    /// <summary>
    /// Gets the current user from the authentication state.
    /// </summary>
    public User? GetCurrentUser()
    {
        var principal = _currentState.User;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        if (int.TryParse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId) &&
            principal.FindFirst(ClaimTypes.Email)?.Value is string email)
        {
            var name = principal.FindFirst(ClaimTypes.Name)?.Value;
            var isAdmin = bool.TryParse(principal.FindFirst("IsAdmin")?.Value, out var admin) && admin;

            return new User
            {
                Id = userId,
                Email = email,
                Name = name,
                IsAdmin = isAdmin
            };
        }

        return null;
    }

    /// <summary>
    /// Checks if the current user is authenticated.
    /// </summary>
    public bool IsAuthenticated()
    {
        return _currentState.User?.Identity?.IsAuthenticated == true;
    }

    /// <summary>
    /// Checks if the current user is an administrator.
    /// </summary>
    public bool IsAdmin()
    {
        return _currentState.User?.FindFirst("IsAdmin")?.Value == "True";
    }
}