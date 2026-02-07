using System.Collections.Concurrent;

namespace HostCraft.Web.Services;

/// <summary>
/// Singleton token store for managing JWT tokens across different scopes.
/// This allows SignalR hubs and other services to access tokens cached during login.
/// Tokens are keyed by a session identifier (circuit ID or connection ID).
/// </summary>
public interface ITokenStore
{
    void SetTokens(string sessionId, string token, string refreshToken);
    (string? Token, string? RefreshToken) GetTokens(string sessionId);
    void ClearTokens(string sessionId);
    string? GetAnyValidToken();
}

public class TokenStore : ITokenStore
{
    private readonly ConcurrentDictionary<string, TokenEntry> _tokens = new();
    private readonly ILogger<TokenStore> _logger;

    // Keep track of the most recently set token for fallback
    private volatile string? _lastToken;
    private volatile string? _lastRefreshToken;

    public TokenStore(ILogger<TokenStore> logger)
    {
        _logger = logger;
    }

    public void SetTokens(string sessionId, string token, string refreshToken)
    {
        var entry = new TokenEntry
        {
            Token = token,
            RefreshToken = refreshToken,
            SetAt = DateTime.UtcNow
        };

        _tokens[sessionId] = entry;
        _lastToken = token;
        _lastRefreshToken = refreshToken;

        _logger.LogInformation("TokenStore: Token SET for session {SessionId}, token length: {Length}",
            sessionId, token?.Length ?? 0);

        // Clean up old entries (older than 24 hours)
        CleanupOldEntries();
    }

    public (string? Token, string? RefreshToken) GetTokens(string sessionId)
    {
        if (_tokens.TryGetValue(sessionId, out var entry))
        {
            return (entry.Token, entry.RefreshToken);
        }

        return (null, null);
    }

    public void ClearTokens(string sessionId)
    {
        _tokens.TryRemove(sessionId, out _);
        _logger.LogDebug("Tokens cleared for session {SessionId}", sessionId);
    }

    /// <summary>
    /// Gets any valid token (used by SignalR hubs that may not have session context).
    /// Returns the most recently set token.
    /// </summary>
    public string? GetAnyValidToken()
    {
        _logger.LogInformation("TokenStore: GetAnyValidToken called, hasToken: {HasToken}, length: {Length}",
            !string.IsNullOrEmpty(_lastToken), _lastToken?.Length ?? 0);
        return _lastToken;
    }

    private void CleanupOldEntries()
    {
        var cutoff = DateTime.UtcNow.AddHours(-24);
        var keysToRemove = _tokens
            .Where(kvp => kvp.Value.SetAt < cutoff)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in keysToRemove)
        {
            _tokens.TryRemove(key, out _);
        }

        if (keysToRemove.Count > 0)
        {
            _logger.LogDebug("Cleaned up {Count} expired token entries", keysToRemove.Count);
        }
    }

    private class TokenEntry
    {
        public string Token { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public DateTime SetAt { get; set; }
    }
}
