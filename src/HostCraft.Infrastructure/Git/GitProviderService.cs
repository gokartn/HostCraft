using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Http;
using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using HostCraft.Core.Interfaces;
using HostCraft.Infrastructure.Persistence;
using System.Net.Http.Headers;
using System.Text.Json;

namespace HostCraft.Infrastructure.Git;

public class GitProviderService : IGitProviderService
{
    private readonly HostCraftDbContext _context;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GitProviderService> _logger;

    public GitProviderService(
        HostCraftDbContext context,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<GitProviderService> logger)
    {
        _context = context;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<string> GetAuthorizationUrlAsync(GitProviderType type, string redirectUri, string? apiUrl = null)
    {
        return type switch
        {
            GitProviderType.GitHub => GetGitHubAuthUrl(redirectUri),
            GitProviderType.GitLab => GetGitLabAuthUrl(redirectUri, apiUrl),
            GitProviderType.Bitbucket => GetBitbucketAuthUrl(redirectUri),
            GitProviderType.Gitea => GetGiteaAuthUrl(redirectUri, apiUrl!),
            _ => throw new NotSupportedException($"Provider type {type} not supported")
        };
    }

    public async Task<GitProvider> ConnectProviderAsync(
        GitProviderType type,
        string code,
        string redirectUri,
        int userId,
        string? apiUrl = null)
    {
        return type switch
        {
            GitProviderType.GitHub => await ConnectGitHubAsync(code, redirectUri, userId),
            GitProviderType.GitLab => await ConnectGitLabAsync(code, redirectUri, userId, apiUrl),
            GitProviderType.Bitbucket => await ConnectBitbucketAsync(code, redirectUri, userId),
            GitProviderType.Gitea => await ConnectGiteaAsync(code, redirectUri, userId, apiUrl!),
            _ => throw new NotSupportedException($"Provider type {type} not supported")
        };
    }

    public async Task<bool> RefreshTokenAsync(int providerId)
    {
        var provider = await _context.GitProviders.FindAsync(providerId);
        if (provider == null) return false;

        // Only some providers support refresh tokens
        if (string.IsNullOrEmpty(provider.RefreshToken))
            return false;

        // TODO: Implement refresh token logic per provider
        return false;
    }

    public async Task<List<GitRepository>> GetRepositoriesAsync(int providerId)
    {
        var provider = await _context.GitProviders.FindAsync(providerId);
        if (provider == null)
            throw new ArgumentException("Provider not found", nameof(providerId));

        return provider.Type switch
        {
            GitProviderType.GitHub => await GetGitHubRepositoriesAsync(provider),
            GitProviderType.GitLab => await GetGitLabRepositoriesAsync(provider),
            GitProviderType.Bitbucket => await GetBitbucketRepositoriesAsync(provider),
            GitProviderType.Gitea => await GetGiteaRepositoriesAsync(provider),
            _ => throw new NotSupportedException($"Provider type {provider.Type} not supported")
        };
    }

    public async Task<List<string>> GetBranchesAsync(int providerId, string owner, string repo)
    {
        var provider = await _context.GitProviders.FindAsync(providerId);
        if (provider == null)
            throw new ArgumentException("Provider not found", nameof(providerId));

        return provider.Type switch
        {
            GitProviderType.GitHub => await GetGitHubBranchesAsync(provider, owner, repo),
            GitProviderType.GitLab => await GetGitLabBranchesAsync(provider, owner, repo),
            GitProviderType.Bitbucket => await GetBitbucketBranchesAsync(provider, owner, repo),
            GitProviderType.Gitea => await GetGiteaBranchesAsync(provider, owner, repo),
            _ => throw new NotSupportedException($"Provider type {provider.Type} not supported")
        };
    }

    public async Task<GitCommit?> GetLatestCommitAsync(int providerId, string owner, string repo, string branch)
    {
        var provider = await _context.GitProviders.FindAsync(providerId);
        if (provider == null) return null;

        // TODO: Implement per provider
        return null;
    }

    public async Task<bool> TestConnectionAsync(int providerId)
    {
        try
        {
            var provider = await _context.GitProviders.FindAsync(providerId);
            if (provider == null) return false;

            // Try to fetch user info to test connection
            var client = CreateAuthenticatedClient(provider);
            var response = await client.GetAsync(GetUserApiUrl(provider));
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> DisconnectProviderAsync(int providerId)
    {
        var provider = await _context.GitProviders.FindAsync(providerId);
        if (provider == null) return false;

        _context.GitProviders.Remove(provider);
        await _context.SaveChangesAsync();
        return true;
    }

    // GitHub implementation
    private string GetGitHubAuthUrl(string redirectUri)
    {
        var clientId = _configuration["GitHub:ClientId"] ?? throw new InvalidOperationException("GitHub ClientId not configured");
        var scope = "repo,read:user,user:email";
        var state = "github";
        
        return $"https://github.com/login/oauth/authorize?client_id={clientId}&redirect_uri={Uri.EscapeDataString(redirectUri)}&scope={scope}&state={state}";
    }

    private async Task<GitProvider> ConnectGitHubAsync(string code, string redirectUri, int userId)
    {
        var clientId = _configuration["GitHub:ClientId"] ?? throw new InvalidOperationException("GitHub ClientId not configured");
        var clientSecret = _configuration["GitHub:ClientSecret"] ?? throw new InvalidOperationException("GitHub ClientSecret not configured");

        // Exchange code for token
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        
        var tokenResponse = await client.PostAsync("https://github.com/login/oauth/access_token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["code"] = code,
            ["redirect_uri"] = redirectUri
        }));

        tokenResponse.EnsureSuccessStatusCode();
        var tokenData = await tokenResponse.Content.ReadFromJsonAsync<GitHubTokenResponse>();
        if (tokenData?.AccessToken == null)
            throw new InvalidOperationException("Failed to obtain access token");

        // Get user info
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenData.AccessToken);
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("HostCraft", "1.0"));
        
        var userResponse = await client.GetAsync("https://api.github.com/user");
        userResponse.EnsureSuccessStatusCode();
        var userData = await userResponse.Content.ReadFromJsonAsync<GitHubUser>();

        var provider = new GitProvider
        {
            UserId = userId,
            Type = GitProviderType.GitHub,
            Name = $"GitHub - {userData?.Login ?? "Unknown"}",
            Username = userData?.Login ?? "Unknown",
            AvatarUrl = userData?.AvatarUrl,
            Email = userData?.Email,
            AccessToken = tokenData.AccessToken,
            Scopes = tokenData.Scope,
            ProviderId = userData?.Id.ToString(),
            ConnectedAt = DateTime.UtcNow,
            IsActive = true
        };

        _context.GitProviders.Add(provider);
        await _context.SaveChangesAsync();

        return provider;
    }

    private async Task<List<GitRepository>> GetGitHubRepositoriesAsync(GitProvider provider)
    {
        var client = CreateAuthenticatedClient(provider);
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("HostCraft", "1.0"));
        
        var response = await client.GetAsync("https://api.github.com/user/repos?per_page=100&sort=updated");
        response.EnsureSuccessStatusCode();
        
        var repos = await response.Content.ReadFromJsonAsync<List<GitHubRepository>>();
        
        return repos?.Select(r => new GitRepository
        {
            Owner = r.Owner.Login,
            Name = r.Name,
            FullName = r.FullName,
            Description = r.Description,
            DefaultBranch = r.DefaultBranch,
            CloneUrl = r.CloneUrl,
            IsPrivate = r.Private,
            UpdatedAt = r.UpdatedAt
        }).ToList() ?? new List<GitRepository>();
    }

    private async Task<List<string>> GetGitHubBranchesAsync(GitProvider provider, string owner, string repo)
    {
        var client = CreateAuthenticatedClient(provider);
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("HostCraft", "1.0"));
        
        var response = await client.GetAsync($"https://api.github.com/repos/{owner}/{repo}/branches");
        response.EnsureSuccessStatusCode();
        
        var branches = await response.Content.ReadFromJsonAsync<List<GitHubBranch>>();
        return branches?.Select(b => b.Name).ToList() ?? new List<string>();
    }

    // Placeholder implementations for other providers
    private string GetGitLabAuthUrl(string redirectUri, string? apiUrl) => throw new NotImplementedException("GitLab support coming soon");
    private string GetBitbucketAuthUrl(string redirectUri) => throw new NotImplementedException("Bitbucket support coming soon");
    private string GetGiteaAuthUrl(string redirectUri, string apiUrl) => throw new NotImplementedException("Gitea support coming soon");
    
    private Task<GitProvider> ConnectGitLabAsync(string code, string redirectUri, int userId, string? apiUrl) => throw new NotImplementedException();
    private Task<GitProvider> ConnectBitbucketAsync(string code, string redirectUri, int userId) => throw new NotImplementedException();
    private Task<GitProvider> ConnectGiteaAsync(string code, string redirectUri, int userId, string apiUrl) => throw new NotImplementedException();
    
    private Task<List<GitRepository>> GetGitLabRepositoriesAsync(GitProvider provider) => throw new NotImplementedException();
    private Task<List<GitRepository>> GetBitbucketRepositoriesAsync(GitProvider provider) => throw new NotImplementedException();
    private Task<List<GitRepository>> GetGiteaRepositoriesAsync(GitProvider provider) => throw new NotImplementedException();
    
    private Task<List<string>> GetGitLabBranchesAsync(GitProvider provider, string owner, string repo) => throw new NotImplementedException();
    private Task<List<string>> GetBitbucketBranchesAsync(GitProvider provider, string owner, string repo) => throw new NotImplementedException();
    private Task<List<string>> GetGiteaBranchesAsync(GitProvider provider, string owner, string repo) => throw new NotImplementedException();

    private HttpClient CreateAuthenticatedClient(GitProvider provider)
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", provider.AccessToken);
        return client;
    }

    private string GetUserApiUrl(GitProvider provider)
    {
        return provider.Type switch
        {
            GitProviderType.GitHub => "https://api.github.com/user",
            GitProviderType.GitLab => $"{provider.ApiUrl ?? "https://gitlab.com"}/api/v4/user",
            GitProviderType.Bitbucket => "https://api.bitbucket.org/2.0/user",
            GitProviderType.Gitea => $"{provider.ApiUrl}/api/v1/user",
            _ => throw new NotSupportedException()
        };
    }

    // GitHub API models
    private class GitHubTokenResponse
    {
        public string? AccessToken { get; set; }
        public string? Scope { get; set; }
        public string? TokenType { get; set; }
    }

    private class GitHubUser
    {
        public long Id { get; set; }
        public string Login { get; set; } = "";
        public string? Email { get; set; }
        public string? AvatarUrl { get; set; }
    }

    private class GitHubRepository
    {
        public string Name { get; set; } = "";
        public string FullName { get; set; } = "";
        public string? Description { get; set; }
        public string DefaultBranch { get; set; } = "main";
        public string CloneUrl { get; set; } = "";
        public bool Private { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public GitHubOwner Owner { get; set; } = new();
    }

    private class GitHubOwner
    {
        public string Login { get; set; } = "";
    }

    private class GitHubBranch
    {
        public string Name { get; set; } = "";
    }
}
