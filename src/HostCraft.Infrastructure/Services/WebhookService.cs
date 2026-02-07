using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using HostCraft.Core.Interfaces;
using HostCraft.Core.Interfaces.Repositories;
using Microsoft.Extensions.Logging;

namespace HostCraft.Infrastructure.Services;

/// <summary>
/// Service for processing GitHub webhook events.
/// Extracted from GitHubWebhookController to follow single responsibility principle.
/// </summary>
public class WebhookService : IWebhookService
{
    private readonly IDeploymentRepository _deploymentRepository;
    private readonly IDockerService _dockerService;
    private readonly IDeploymentJobQueue _deploymentJobQueue;
    private readonly ILogger<WebhookService> _logger;

    public WebhookService(
        IDeploymentRepository deploymentRepository,
        IDockerService dockerService,
        IDeploymentJobQueue deploymentJobQueue,
        ILogger<WebhookService> logger)
    {
        _deploymentRepository = deploymentRepository;
        _dockerService = dockerService;
        _deploymentJobQueue = deploymentJobQueue;
        _logger = logger;
    }

    public bool VerifyGitHubSignature(string body, string? signature, string? secret)
    {
        if (string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(secret))
            return false;

        // Remove "sha256=" prefix
        if (!signature.StartsWith("sha256="))
            return false;

        var signatureHash = signature.Substring(7);

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        var computedSignature = Convert.ToHexString(hash).ToLower();

        return signatureHash == computedSignature;
    }

    public async Task<WebhookProcessingResult> HandlePushEventAsync(Application application, JsonElement payload, CancellationToken cancellationToken = default)
    {
        // Get ref (branch)
        var refValue = payload.GetProperty("ref").GetString();
        var branch = refValue?.Replace("refs/heads/", "");

        // Check if this is the configured branch
        if (branch != application.GitBranch)
        {
            _logger.LogInformation(
                "Push to {Branch} ignored, configured branch is {ConfiguredBranch}",
                branch,
                application.GitBranch);
            return new WebhookProcessingResult(false, "Branch not configured for deployment");
        }

        // Check if auto-deploy is enabled
        if (!application.AutoDeploy || !application.AutoDeployOnPush)
        {
            _logger.LogInformation("Auto-deploy disabled for {App}", application.Name);
            return new WebhookProcessingResult(false, "Auto-deploy disabled");
        }

        // Get commit info
        var headCommit = payload.GetProperty("head_commit");
        var commitSha = headCommit.GetProperty("id").GetString();
        var commitMessage = headCommit.GetProperty("message").GetString();
        var commitAuthor = headCommit.GetProperty("author").GetProperty("name").GetString();

        // Check for skip keywords in commit message
        if (ShouldSkipDeployment(commitMessage))
        {
            _logger.LogInformation("Deployment skipped due to commit message: {Message}", commitMessage);
            return new WebhookProcessingResult(false, "Deployment skipped by commit message");
        }

        // Check watch paths if configured
        if (!string.IsNullOrEmpty(application.WatchPaths))
        {
            var commits = payload.GetProperty("commits");
            if (!HasChangesInWatchPaths(commits, application.WatchPaths))
            {
                _logger.LogInformation("No changes in watched paths for {App}", application.Name);
                return new WebhookProcessingResult(false, "No changes in watched paths");
            }
        }

        // Create deployment
        var deployment = new Deployment
        {
            ApplicationId = application.Id,
            Status = DeploymentStatus.Queued,
            CommitSha = commitSha,
            CommitMessage = commitMessage,
            CommitAuthor = commitAuthor,
            TriggeredBy = "GitHub Webhook",
            CreatedAt = DateTime.UtcNow
        };

        deployment = await _deploymentRepository.AddAsync(deployment, cancellationToken);

        _logger.LogInformation(
            "Created deployment {DeploymentId} for {App} from commit {CommitSha}",
            deployment.Id,
            application.Name,
            commitSha?.Substring(0, 7));

        // Queue build and deployment for background processing
        await _deploymentJobQueue.EnqueueAsync(new HostCraft.Core.Models.DeploymentJob(HostCraft.Core.Models.DeploymentJobType.Deploy, deployment.Id), cancellationToken);

        return new WebhookProcessingResult(true, "Deployment queued", deployment.Id, commitSha);
    }

    public async Task<WebhookProcessingResult> HandlePullRequestEventAsync(Application application, JsonElement payload, CancellationToken cancellationToken = default)
    {
        if (!application.EnablePreviewDeployments)
        {
            return new WebhookProcessingResult(false, "Preview deployments disabled");
        }

        var action = payload.GetProperty("action").GetString();
        var pullRequest = payload.GetProperty("pull_request");
        var prNumber = pullRequest.GetProperty("number").GetInt32();
        var prBranch = pullRequest.GetProperty("head").GetProperty("ref").GetString();
        var prBaseBranch = pullRequest.GetProperty("base").GetProperty("ref").GetString();

        // Only deploy to PRs targeting the configured branch
        if (prBaseBranch != application.GitBranch)
        {
            return new WebhookProcessingResult(false, "PR not targeting configured branch");
        }

        if (action == "opened" || action == "synchronize" || action == "reopened")
        {
            _logger.LogInformation(
                "Pull request #{PrNumber} {Action} for {App}",
                prNumber,
                action,
                application.Name);

            // Create preview deployment
            var commitSha = pullRequest.GetProperty("head").GetProperty("sha").GetString();
            var commitMessage = $"PR #{prNumber}: {pullRequest.GetProperty("title").GetString()}";

            var deployment = new Deployment
            {
                ApplicationId = application.Id,
                Status = DeploymentStatus.Queued,
                CommitSha = commitSha,
                CommitMessage = commitMessage,
                CommitAuthor = pullRequest.GetProperty("user").GetProperty("login").GetString(),
                TriggeredBy = $"GitHub PR #{prNumber}",
                IsPreview = true,
                PreviewId = $"pr-{prNumber}",
                CreatedAt = DateTime.UtcNow
            };

            deployment = await _deploymentRepository.AddAsync(deployment, cancellationToken);

            // Queue build
            await _deploymentJobQueue.EnqueueAsync(new HostCraft.Core.Models.DeploymentJob(HostCraft.Core.Models.DeploymentJobType.Deploy, deployment.Id), cancellationToken);

            var previewUrl = $"https://pr-{prNumber}-{application.Domain}";
            return new WebhookProcessingResult(true, "Preview deployment queued", deployment.Id, commitSha, previewUrl);
        }

        if (action == "closed")
        {
            _logger.LogInformation(
                "Pull request #{PrNumber} closed for {App}, cleaning up preview",
                prNumber,
                application.Name);

            // Clean up preview deployment
            var previewId = $"pr-{prNumber}";
            await _deploymentJobQueue.EnqueueAsync(
                new HostCraft.Core.Models.DeploymentJob(
                    HostCraft.Core.Models.DeploymentJobType.CleanupPreview,
                    0,
                    application.Id,
                    previewId),
                cancellationToken);

            return new WebhookProcessingResult(true, "Preview deployment cleanup queued");
        }

        return new WebhookProcessingResult(false, $"PR action {action} not handled");
    }

    public bool ShouldSkipDeployment(string? commitMessage)
    {
        if (string.IsNullOrEmpty(commitMessage))
            return false;

        var skipKeywords = new[]
        {
            "[skip ci]",
            "[ci skip]",
            "[no ci]",
            "[skip actions]",
            "[actions skip]"
        };

        return skipKeywords.Any(keyword =>
            commitMessage.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private bool HasChangesInWatchPaths(JsonElement commits, string watchPaths)
    {
        var watchedPaths = watchPaths.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .ToList();

        if (!watchedPaths.Any())
            return true;

        foreach (var commit in commits.EnumerateArray())
        {
            var changedFiles = new List<string>();

            if (commit.TryGetProperty("added", out var added))
                changedFiles.AddRange(added.EnumerateArray().Select(f => f.GetString()!));

            if (commit.TryGetProperty("modified", out var modified))
                changedFiles.AddRange(modified.EnumerateArray().Select(f => f.GetString()!));

            if (commit.TryGetProperty("removed", out var removed))
                changedFiles.AddRange(removed.EnumerateArray().Select(f => f.GetString()!));

            // Check if any changed file matches watched paths
            foreach (var file in changedFiles)
            {
                if (watchedPaths.Any(path => file.StartsWith(path, StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
        }

        return false;
    }

}
