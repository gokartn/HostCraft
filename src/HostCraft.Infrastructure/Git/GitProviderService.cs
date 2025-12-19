using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using HostCraft.Core.Interfaces;
using HostCraft.Infrastructure.Persistence;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
        // Get credentials from database first, fall back to configuration
        var settings = await _context.GitProviderSettings
            .FirstOrDefaultAsync(s => s.Type == type && s.ApiUrl == apiUrl);

        return type switch
        {
            GitProviderType.GitHub => GetGitHubAuthUrl(redirectUri, settings),
            GitProviderType.GitLab => GetGitLabAuthUrl(redirectUri, apiUrl, settings),
            GitProviderType.Bitbucket => GetBitbucketAuthUrl(redirectUri, settings),
            GitProviderType.Gitea => GetGiteaAuthUrl(redirectUri, apiUrl!, settings),
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
        // Get credentials from database first, fall back to configuration
        var settings = await _context.GitProviderSettings
            .FirstOrDefaultAsync(s => s.Type == type && s.ApiUrl == apiUrl);

        return type switch
        {
            GitProviderType.GitHub => await ConnectGitHubAsync(code, redirectUri, userId, settings),
            GitProviderType.GitLab => await ConnectGitLabAsync(code, redirectUri, userId, apiUrl, settings),
            GitProviderType.Bitbucket => await ConnectBitbucketAsync(code, redirectUri, userId, settings),
            GitProviderType.Gitea => await ConnectGiteaAsync(code, redirectUri, userId, apiUrl!, settings),
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

        try
        {
            return provider.Type switch
            {
                GitProviderType.GitHub => await RefreshGitHubTokenAsync(provider),
                GitProviderType.GitLab => await RefreshGitLabTokenAsync(provider),
                GitProviderType.Bitbucket => await RefreshBitbucketTokenAsync(provider),
                GitProviderType.Gitea => false, // Gitea uses personal access tokens, no refresh needed
                _ => false
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh token for provider {ProviderId}", providerId);
            return false;
        }
    }

    private async Task<bool> RefreshGitHubTokenAsync(GitProvider provider)
    {
        // GitHub OAuth tokens don't typically expire, but we can validate them
        var client = CreateAuthenticatedClient(provider);
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("HostCraft", "1.0"));
        var response = await client.GetAsync("https://api.github.com/user");
        return response.IsSuccessStatusCode;
    }

    private async Task<bool> RefreshGitLabTokenAsync(GitProvider provider)
    {
        if (string.IsNullOrEmpty(provider.RefreshToken))
            return false;

        var clientId = _configuration["GitLab:ClientId"];
        var clientSecret = _configuration["GitLab:ClientSecret"];
        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            return false;

        var client = _httpClientFactory.CreateClient();
        var apiUrl = provider.ApiUrl ?? "https://gitlab.com";

        var response = await client.PostAsync($"{apiUrl}/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["refresh_token"] = provider.RefreshToken,
            ["grant_type"] = "refresh_token"
        }));

        if (!response.IsSuccessStatusCode)
            return false;

        var tokenData = await response.Content.ReadFromJsonAsync<GitLabTokenResponse>();
        if (tokenData?.AccessToken == null)
            return false;

        provider.AccessToken = tokenData.AccessToken;
        provider.RefreshToken = tokenData.RefreshToken;
        await _context.SaveChangesAsync();

        return true;
    }

    private async Task<bool> RefreshBitbucketTokenAsync(GitProvider provider)
    {
        if (string.IsNullOrEmpty(provider.RefreshToken))
            return false;

        var clientId = _configuration["Bitbucket:ClientId"];
        var clientSecret = _configuration["Bitbucket:ClientSecret"];
        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            return false;

        var client = _httpClientFactory.CreateClient();
        var authHeader = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{clientId}:{clientSecret}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authHeader);

        var response = await client.PostAsync("https://bitbucket.org/site/oauth2/access_token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = provider.RefreshToken
        }));

        if (!response.IsSuccessStatusCode)
            return false;

        var tokenData = await response.Content.ReadFromJsonAsync<BitbucketTokenResponse>();
        if (tokenData?.AccessToken == null)
            return false;

        provider.AccessToken = tokenData.AccessToken;
        provider.RefreshToken = tokenData.RefreshToken;
        await _context.SaveChangesAsync();

        return true;
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

        return provider.Type switch
        {
            GitProviderType.GitHub => await GetGitHubLatestCommitAsync(provider, owner, repo, branch),
            GitProviderType.GitLab => await GetGitLabLatestCommitAsync(provider, owner, repo, branch),
            GitProviderType.Bitbucket => await GetBitbucketLatestCommitAsync(provider, owner, repo, branch),
            GitProviderType.Gitea => await GetGiteaLatestCommitAsync(provider, owner, repo, branch),
            _ => null
        };
    }

    private async Task<GitCommit?> GetGitHubLatestCommitAsync(GitProvider provider, string owner, string repo, string branch)
    {
        var client = CreateAuthenticatedClient(provider);
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("HostCraft", "1.0"));

        var response = await client.GetAsync($"https://api.github.com/repos/{owner}/{repo}/commits/{branch}");
        if (!response.IsSuccessStatusCode) return null;

        var commit = await response.Content.ReadFromJsonAsync<GitHubCommitResponse>();
        if (commit == null) return null;

        return new GitCommit
        {
            Sha = commit.Sha,
            Message = commit.Commit?.Message,
            Author = commit.Commit?.Author?.Name,
            AuthorEmail = commit.Commit?.Author?.Email,
            Date = commit.Commit?.Author?.Date ?? DateTime.UtcNow
        };
    }

    private async Task<GitCommit?> GetGitLabLatestCommitAsync(GitProvider provider, string owner, string repo, string branch)
    {
        var client = CreateAuthenticatedClient(provider);
        var apiUrl = provider.ApiUrl ?? "https://gitlab.com";
        var projectPath = Uri.EscapeDataString($"{owner}/{repo}");

        var response = await client.GetAsync($"{apiUrl}/api/v4/projects/{projectPath}/repository/commits/{branch}");
        if (!response.IsSuccessStatusCode) return null;

        var commit = await response.Content.ReadFromJsonAsync<GitLabCommitResponse>();
        if (commit == null) return null;

        return new GitCommit
        {
            Sha = commit.Id,
            Message = commit.Message,
            Author = commit.AuthorName,
            AuthorEmail = commit.AuthorEmail,
            Date = commit.CreatedAt ?? DateTime.UtcNow
        };
    }

    private async Task<GitCommit?> GetBitbucketLatestCommitAsync(GitProvider provider, string owner, string repo, string branch)
    {
        var client = CreateAuthenticatedClient(provider);

        var response = await client.GetAsync($"https://api.bitbucket.org/2.0/repositories/{owner}/{repo}/commits/{branch}?pagelen=1");
        if (!response.IsSuccessStatusCode) return null;

        var result = await response.Content.ReadFromJsonAsync<BitbucketCommitsResponse>();
        var commit = result?.Values?.FirstOrDefault();
        if (commit == null) return null;

        return new GitCommit
        {
            Sha = commit.Hash,
            Message = commit.Message,
            Author = commit.Author?.User?.DisplayName ?? commit.Author?.Raw,
            AuthorEmail = null,
            Date = commit.Date ?? DateTime.UtcNow
        };
    }

    private async Task<GitCommit?> GetGiteaLatestCommitAsync(GitProvider provider, string owner, string repo, string branch)
    {
        var client = CreateAuthenticatedClient(provider);
        var apiUrl = provider.ApiUrl;

        var response = await client.GetAsync($"{apiUrl}/api/v1/repos/{owner}/{repo}/commits?sha={branch}&limit=1");
        if (!response.IsSuccessStatusCode) return null;

        var commits = await response.Content.ReadFromJsonAsync<List<GiteaCommitResponse>>();
        var commit = commits?.FirstOrDefault();
        if (commit == null) return null;

        return new GitCommit
        {
            Sha = commit.Sha,
            Message = commit.Commit?.Message,
            Author = commit.Commit?.Author?.Name,
            AuthorEmail = commit.Commit?.Author?.Email,
            Date = commit.Commit?.Author?.Date ?? DateTime.UtcNow
        };
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

    public async Task<bool> RegisterWebhookAsync(Application application, string webhookUrl, string webhookSecret)
    {
        if (application.GitProvider == null)
        {
            var provider = await _context.GitProviders.FindAsync(application.GitProviderId);
            if (provider == null) return false;
            application.GitProvider = provider;
        }

        return application.GitProvider.Type switch
        {
            GitProviderType.GitHub => await RegisterGitHubWebhookAsync(application, webhookUrl, webhookSecret),
            _ => throw new NotSupportedException($"Webhook registration for {application.GitProvider.Type} not yet implemented")
        };
    }

    public async Task<bool> UnregisterWebhookAsync(Application application)
    {
        if (application.GitProvider == null)
        {
            var provider = await _context.GitProviders.FindAsync(application.GitProviderId);
            if (provider == null) return false;
            application.GitProvider = provider;
        }

        return application.GitProvider.Type switch
        {
            GitProviderType.GitHub => await UnregisterGitHubWebhookAsync(application),
            _ => false // Other providers not yet supported for webhook deletion
        };
    }

    private async Task<bool> UnregisterGitHubWebhookAsync(Application application)
    {
        try
        {
            var client = CreateAuthenticatedClient(application.GitProvider!);
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("HostCraft", "1.0"));

            // List webhooks to find ours
            var response = await client.GetAsync(
                $"https://api.github.com/repos/{application.GitOwner}/{application.GitRepoName}/hooks");

            if (!response.IsSuccessStatusCode)
                return false;

            var webhooks = await response.Content.ReadFromJsonAsync<List<GitHubWebhook>>();
            if (webhooks == null) return false;

            // Find webhooks that point to our application
            var hostcraftWebhooks = webhooks.Where(w =>
                w.Config?.Url?.Contains(application.Uuid.ToString()) == true ||
                w.Config?.Url?.Contains("hostcraft") == true).ToList();

            foreach (var webhook in hostcraftWebhooks)
            {
                var deleteResponse = await client.DeleteAsync(
                    $"https://api.github.com/repos/{application.GitOwner}/{application.GitRepoName}/hooks/{webhook.Id}");

                if (deleteResponse.IsSuccessStatusCode)
                {
                    _logger.LogInformation(
                        "Deleted webhook {WebhookId} for {Owner}/{Repo}",
                        webhook.Id,
                        application.GitOwner,
                        application.GitRepoName);
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unregistering webhook for {Owner}/{Repo}", application.GitOwner, application.GitRepoName);
            return false;
        }
    }

    private class GitHubWebhook
    {
        public long Id { get; set; }
        public GitHubWebhookConfig? Config { get; set; }
    }

    private class GitHubWebhookConfig
    {
        public string? Url { get; set; }
    }

    private async Task<bool> RegisterGitHubWebhookAsync(Application application, string webhookUrl, string webhookSecret)
    {
        try
        {
            var client = CreateAuthenticatedClient(application.GitProvider!);
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("HostCraft", "1.0"));

            var payload = new
            {
                name = "web",
                active = true,
                events = new[] { "push", "pull_request" },
                config = new
                {
                    url = webhookUrl,
                    content_type = "json",
                    secret = webhookSecret,
                    insecure_ssl = "0"
                }
            };

            var response = await client.PostAsJsonAsync(
                $"https://api.github.com/repos/{application.GitOwner}/{application.GitRepoName}/hooks",
                payload);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "Registered webhook for {Owner}/{Repo}",
                    application.GitOwner,
                    application.GitRepoName);
                return true;
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogWarning(
                    "Failed to register webhook: {StatusCode} - {Error}",
                    response.StatusCode,
                    error);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering webhook for {Owner}/{Repo}", application.GitOwner, application.GitRepoName);
            return false;
        }
    }

    // Helper to get credentials from database or config
    private (string clientId, string clientSecret) GetCredentials(GitProviderSettings? settings, string providerName)
    {
        // Try database settings first
        if (settings != null && settings.IsConfigured && settings.IsEnabled)
        {
            return (settings.ClientId!, settings.ClientSecret!);
        }

        // Fall back to configuration (for backwards compatibility)
        var clientId = _configuration[$"{providerName}:ClientId"];
        var clientSecret = _configuration[$"{providerName}:ClientSecret"];

        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
        {
            throw new InvalidOperationException(
                $"{providerName} OAuth not configured. Please configure {providerName} credentials in Settings → Git Providers.");
        }

        return (clientId, clientSecret);
    }

    // GitHub implementation
    private string GetGitHubAuthUrl(string redirectUri, GitProviderSettings? settings)
    {
        var (clientId, _) = GetCredentials(settings, "GitHub");
        var scope = "repo,read:user,user:email";
        var state = "github";

        return $"https://github.com/login/oauth/authorize?client_id={clientId}&redirect_uri={Uri.EscapeDataString(redirectUri)}&scope={scope}&state={state}";
    }

    private async Task<GitProvider> ConnectGitHubAsync(string code, string redirectUri, int userId, GitProviderSettings? settings)
    {
        var (clientId, clientSecret) = GetCredentials(settings, "GitHub");

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
    private string GetGitLabAuthUrl(string redirectUri, string? apiUrl, GitProviderSettings? settings) => throw new NotImplementedException("GitLab support coming soon");
    private string GetBitbucketAuthUrl(string redirectUri, GitProviderSettings? settings) => throw new NotImplementedException("Bitbucket support coming soon");
    private string GetGiteaAuthUrl(string redirectUri, string apiUrl, GitProviderSettings? settings) => throw new NotImplementedException("Gitea support coming soon");

    private Task<GitProvider> ConnectGitLabAsync(string code, string redirectUri, int userId, string? apiUrl, GitProviderSettings? settings) => throw new NotImplementedException();
    private Task<GitProvider> ConnectBitbucketAsync(string code, string redirectUri, int userId, GitProviderSettings? settings) => throw new NotImplementedException();
    private Task<GitProvider> ConnectGiteaAsync(string code, string redirectUri, int userId, string apiUrl, GitProviderSettings? settings) => throw new NotImplementedException();
    
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

    // Additional API response models for commit retrieval
    private class GitHubCommitResponse
    {
        public string Sha { get; set; } = "";
        public GitHubCommitData? Commit { get; set; }
    }

    private class GitHubCommitData
    {
        public string? Message { get; set; }
        public GitHubCommitAuthor? Author { get; set; }
    }

    private class GitHubCommitAuthor
    {
        public string? Name { get; set; }
        public string? Email { get; set; }
        public DateTime? Date { get; set; }
    }

    private class GitLabTokenResponse
    {
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public int ExpiresIn { get; set; }
    }

    private class GitLabCommitResponse
    {
        public string Id { get; set; } = "";
        public string? Message { get; set; }
        public string? AuthorName { get; set; }
        public string? AuthorEmail { get; set; }
        public DateTime? CreatedAt { get; set; }
    }

    private class BitbucketTokenResponse
    {
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public int ExpiresIn { get; set; }
    }

    private class BitbucketCommitsResponse
    {
        public List<BitbucketCommit>? Values { get; set; }
    }

    private class BitbucketCommit
    {
        public string Hash { get; set; } = "";
        public string? Message { get; set; }
        public DateTime? Date { get; set; }
        public BitbucketAuthor? Author { get; set; }
    }

    private class BitbucketAuthor
    {
        public string? Raw { get; set; }
        public BitbucketUser? User { get; set; }
    }

    private class BitbucketUser
    {
        public string? DisplayName { get; set; }
    }

    private class GiteaCommitResponse
    {
        public string Sha { get; set; } = "";
        public GiteaCommitData? Commit { get; set; }
    }

    private class GiteaCommitData
    {
        public string? Message { get; set; }
        public GiteaCommitAuthor? Author { get; set; }
    }

    private class GiteaCommitAuthor
    {
        public string? Name { get; set; }
        public string? Email { get; set; }
        public DateTime? Date { get; set; }
    }
}
