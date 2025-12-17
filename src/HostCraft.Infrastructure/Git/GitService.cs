using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using HostCraft.Core.Interfaces;
using HostCraft.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HostCraft.Infrastructure.Git;

/// <summary>
/// Implements Git operations using Git CLI.
/// </summary>
public class GitService : IGitService
{
    private readonly HostCraftDbContext _context;
    private readonly ILogger<GitService> _logger;
    private readonly string _tempPath;

    public GitService(
        HostCraftDbContext context,
        ILogger<GitService> logger)
    {
        _context = context;
        _logger = logger;
        _tempPath = Path.Combine(Path.GetTempPath(), "hostcraft-repos");
        
        // Ensure temp directory exists
        Directory.CreateDirectory(_tempPath);
    }

    public async Task<string> CloneRepositoryAsync(
        string repositoryUrl,
        string? branch = null,
        string? username = null,
        string? token = null,
        CancellationToken cancellationToken = default)
    {
        var repoName = GetRepositoryNameFromUrl(repositoryUrl);
        var targetPath = Path.Combine(_tempPath, $"{repoName}-{Guid.NewGuid():N}");

        try
        {
            // Build clone URL with authentication if provided
            var cloneUrl = repositoryUrl;
            if (!string.IsNullOrEmpty(token))
            {
                cloneUrl = InjectTokenIntoUrl(repositoryUrl, token);
            }

            var args = $"clone {(branch != null ? $"-b {branch}" : "")} --depth 1 \"{cloneUrl}\" \"{targetPath}\"";
            
            _logger.LogInformation("Cloning repository {Url} to {Path}", repositoryUrl, targetPath);
            
            await RunGitCommandAsync(args, _tempPath, cancellationToken);

            _logger.LogInformation("Successfully cloned repository to {Path}", targetPath);
            return targetPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clone repository {Url}", repositoryUrl);
            
            // Cleanup on failure
            if (Directory.Exists(targetPath))
            {
                try { Directory.Delete(targetPath, true); }
                catch { /* Ignore cleanup errors */ }
            }

            throw;
        }
    }

    public async Task<string> GetLatestCommitHashAsync(string repositoryPath, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await RunGitCommandAsync("rev-parse HEAD", repositoryPath, cancellationToken);
            return result.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get commit hash from {Path}", repositoryPath);
            throw;
        }
    }

    public async Task<bool> PullLatestAsync(string repositoryPath, CancellationToken cancellationToken = default)
    {
        try
        {
            await RunGitCommandAsync("pull origin", repositoryPath, cancellationToken);
            _logger.LogInformation("Successfully pulled latest changes for {Path}", repositoryPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to pull latest for {Path}", repositoryPath);
            return false;
        }
    }

    public async Task<IEnumerable<string>> GetBranchesAsync(
        string repositoryUrl,
        string? username = null,
        string? token = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var cloneUrl = repositoryUrl;
            if (!string.IsNullOrEmpty(token))
            {
                cloneUrl = InjectTokenIntoUrl(repositoryUrl, token);
            }

            var result = await RunGitCommandAsync($"ls-remote --heads \"{cloneUrl}\"", _tempPath, cancellationToken);
            
            var branches = result.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Split('\t')[1].Replace("refs/heads/", ""))
                .ToList();

            return branches;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get branches for {Url}", repositoryUrl);
            throw;
        }
    }

    public async Task<string> CloneApplicationRepositoryAsync(Application application, string? commitSha = null)
    {
        if (application.GitProvider == null)
        {
            throw new InvalidOperationException("Application does not have a Git provider configured");
        }

        if (string.IsNullOrEmpty(application.GitRepository))
        {
            throw new InvalidOperationException("Application does not have a repository configured");
        }

        var cloneUrl = await GetAuthenticatedCloneUrlAsync(application);
        var repoName = $"{application.GitOwner}-{application.GitRepoName}";
        var targetPath = Path.Combine(_tempPath, $"{repoName}-{Guid.NewGuid():N}");

        try
        {
            // Clone with branch
            var args = $"clone -b {application.GitBranch} ";
            
            // Only clone specific commit depth if specified
            if (commitSha == null)
            {
                args += "--depth 1 ";
            }
            
            if (application.CloneSubmodules)
            {
                args += "--recurse-submodules ";
            }

            args += $"\"{cloneUrl}\" \"{targetPath}\"";

            _logger.LogInformation(
                "Cloning repository {Owner}/{Repo} branch {Branch} to {Path}",
                application.GitOwner,
                application.GitRepoName,
                application.GitBranch,
                targetPath);

            await RunGitCommandAsync(args, _tempPath);

            // Checkout specific commit if provided
            if (!string.IsNullOrEmpty(commitSha))
            {
                _logger.LogInformation("Checking out commit {Sha}", commitSha);
                await RunGitCommandAsync($"checkout {commitSha}", targetPath);
            }

            _logger.LogInformation("Successfully cloned application repository to {Path}", targetPath);
            return targetPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clone application repository");
            
            // Cleanup on failure
            if (Directory.Exists(targetPath))
            {
                try { Directory.Delete(targetPath, true); }
                catch { /* Ignore cleanup errors */ }
            }

            throw;
        }
    }

    public async Task<string> GetAuthenticatedCloneUrlAsync(Application application)
    {
        if (application.GitProvider == null)
        {
            var provider = await _context.GitProviders.FindAsync(application.GitProviderId);
            if (provider == null)
            {
                throw new InvalidOperationException("Git provider not found");
            }
            application.GitProvider = provider;
        }

        var baseUrl = application.GitProvider.Type switch
        {
            GitProviderType.GitHub => "github.com",
            GitProviderType.GitLab => new Uri(application.GitProvider.ApiUrl ?? "https://gitlab.com").Host,
            GitProviderType.Bitbucket => "bitbucket.org",
            GitProviderType.Gitea => new Uri(application.GitProvider.ApiUrl ?? "").Host,
            _ => throw new NotSupportedException($"Provider type {application.GitProvider.Type} not supported")
        };

        // Format: https://oauth2:TOKEN@github.com/owner/repo.git
        var token = application.GitProvider.AccessToken;
        return $"https://oauth2:{token}@{baseUrl}/{application.GitOwner}/{application.GitRepoName}.git";
    }

    public async Task CleanupRepositoryAsync(string repositoryPath)
    {
        await Task.Run(() =>
        {
            if (Directory.Exists(repositoryPath))
            {
                try
                {
                    // Make files writable (Git marks some as read-only)
                    var di = new DirectoryInfo(repositoryPath);
                    foreach (var file in di.GetFiles("*", SearchOption.AllDirectories))
                    {
                        file.Attributes = FileAttributes.Normal;
                    }

                    Directory.Delete(repositoryPath, true);
                    _logger.LogInformation("Cleaned up repository at {Path}", repositoryPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to cleanup repository at {Path}", repositoryPath);
                }
            }
        });
    }

    private async Task<string> RunGitCommandAsync(
        string arguments,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = processInfo };
        
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        process.OutputDataReceived += (sender, e) =>
        {
            if (e.Data != null)
                outputBuilder.AppendLine(e.Data);
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data != null)
                errorBuilder.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var error = errorBuilder.ToString();
            throw new InvalidOperationException($"Git command failed: {error}");
        }

        return outputBuilder.ToString();
    }

    private string GetRepositoryNameFromUrl(string url)
    {
        var uri = new Uri(url);
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var repoName = segments.Last().Replace(".git", "");
        return repoName;
    }

    private string InjectTokenIntoUrl(string url, string token)
    {
        var uri = new Uri(url);
        return $"{uri.Scheme}://oauth2:{token}@{uri.Host}{uri.PathAndQuery}";
    }
}
