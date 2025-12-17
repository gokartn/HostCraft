using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using HostCraft.Core.Interfaces;
using HostCraft.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace HostCraft.Api.Controllers;

/// <summary>
/// Handles GitHub webhook events for automated deployments.
/// </summary>
[ApiController]
[Route("api/webhooks/github")]
public class GitHubWebhookController : ControllerBase
{
    private readonly HostCraftDbContext _context;
    private readonly IGitService _gitService;
    private readonly IBuildService _buildService;
    private readonly ILogger<GitHubWebhookController> _logger;

    public GitHubWebhookController(
        HostCraftDbContext context,
        IGitService gitService,
        IBuildService buildService,
        ILogger<GitHubWebhookController> logger)
    {
        _context = context;
        _gitService = gitService;
        _buildService = buildService;
        _logger = logger;
    }

    [HttpPost("{applicationUuid}")]
    public async Task<IActionResult> HandleWebhook(Guid applicationUuid)
    {
        try
        {
            // Read request body
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();

            // Get application
            var application = await _context.Applications
                .Include(a => a.GitProvider)
                .Include(a => a.Server)
                .FirstOrDefaultAsync(a => a.Uuid == applicationUuid);

            if (application == null)
            {
                _logger.LogWarning("Webhook received for unknown application {Uuid}", applicationUuid);
                return NotFound();
            }

            // Verify webhook signature
            var signature = Request.Headers["X-Hub-Signature-256"].FirstOrDefault();
            if (!VerifySignature(body, signature, application.WebhookSecret))
            {
                _logger.LogWarning("Invalid webhook signature for application {Name}", application.Name);
                return Unauthorized();
            }

            // Get event type
            var eventType = Request.Headers["X-GitHub-Event"].FirstOrDefault();
            _logger.LogInformation("Received GitHub {Event} event for {App}", eventType, application.Name);

            // Handle ping event
            if (eventType == "ping")
            {
                return Ok(new { message = "Webhook configured successfully" });
            }

            // Parse payload
            var payload = JsonSerializer.Deserialize<JsonElement>(body);

            // Handle push events
            if (eventType == "push")
            {
                return await HandlePushEvent(application, payload);
            }

            // Handle pull request events
            if (eventType == "pull_request")
            {
                return await HandlePullRequestEvent(application, payload);
            }

            return Ok(new { message = $"Event {eventType} received but not processed" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing webhook for {Uuid}", applicationUuid);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    private async Task<IActionResult> HandlePushEvent(Application application, JsonElement payload)
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
            return Ok(new { message = "Branch not configured for deployment" });
        }

        // Check if auto-deploy is enabled
        if (!application.AutoDeploy || !application.AutoDeployOnPush)
        {
            _logger.LogInformation("Auto-deploy disabled for {App}", application.Name);
            return Ok(new { message = "Auto-deploy disabled" });
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
            return Ok(new { message = "Deployment skipped by commit message" });
        }

        // Check watch paths if configured
        if (!string.IsNullOrEmpty(application.WatchPaths))
        {
            var commits = payload.GetProperty("commits");
            if (!HasChangesInWatchPaths(commits, application.WatchPaths))
            {
                _logger.LogInformation("No changes in watched paths for {App}", application.Name);
                return Ok(new { message = "No changes in watched paths" });
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

        _context.Deployments.Add(deployment);
        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "Created deployment {DeploymentId} for {App} from commit {CommitSha}",
            deployment.Id,
            application.Name,
            commitSha?.Substring(0, 7));

        // Queue build and deployment
        _ = Task.Run(async () => await ProcessDeployment(deployment.Id));

        return Accepted(new
        {
            message = "Deployment queued",
            deploymentId = deployment.Id,
            commit = commitSha,
            application = application.Name
        });
    }

    private async Task<IActionResult> HandlePullRequestEvent(Application application, JsonElement payload)
    {
        if (!application.EnablePreviewDeployments)
        {
            return Ok(new { message = "Preview deployments disabled" });
        }

        var action = payload.GetProperty("action").GetString();
        var pullRequest = payload.GetProperty("pull_request");
        var prNumber = pullRequest.GetProperty("number").GetInt32();
        var prBranch = pullRequest.GetProperty("head").GetProperty("ref").GetString();
        var prBaseBranch = pullRequest.GetProperty("base").GetProperty("ref").GetString();

        // Only deploy to PRs targeting the configured branch
        if (prBaseBranch != application.GitBranch)
        {
            return Ok(new { message = "PR not targeting configured branch" });
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

            _context.Deployments.Add(deployment);
            await _context.SaveChangesAsync();

            // Queue build
            _ = Task.Run(async () => await ProcessDeployment(deployment.Id));

            return Accepted(new
            {
                message = "Preview deployment queued",
                deploymentId = deployment.Id,
                previewUrl = $"https://pr-{prNumber}-{application.Domain}"
            });
        }

        if (action == "closed")
        {
            _logger.LogInformation(
                "Pull request #{PrNumber} closed for {App}, cleaning up preview",
                prNumber,
                application.Name);

            // TODO: Clean up preview deployment
            return Ok(new { message = "Preview deployment cleanup queued" });
        }

        return Ok(new { message = $"PR action {action} not handled" });
    }

    private async Task ProcessDeployment(int deploymentId)
    {
        try
        {
            var deployment = await _context.Deployments
                .Include(d => d.Application)
                    .ThenInclude(a => a.GitProvider)
                .Include(d => d.Application.Server)
                .FirstOrDefaultAsync(d => d.Id == deploymentId);

            if (deployment == null) return;

            // Update status
            deployment.Status = DeploymentStatus.Running;
            deployment.StartedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // Clone repository
            var sourcePath = await _gitService.CloneApplicationRepositoryAsync(
                deployment.Application,
                deployment.CommitSha ?? deployment.Application.LastCommitSha);

            // Build image
            var imageName = await _buildService.BuildImageAsync(
                deployment.Application,
                sourcePath,
                deployment.CommitSha);

            // Deploy (status remains Running)
            await _context.SaveChangesAsync();

            // TODO: Implement actual deployment logic (docker deploy, service update, etc.)

            // Update deployment
            deployment.Status = DeploymentStatus.Success;
            deployment.FinishedAt = DateTime.UtcNow;
            deployment.Application.LastDeployedAt = DateTime.UtcNow;
            deployment.Application.LastCommitSha = deployment.CommitSha;
            deployment.Application.LastCommitMessage = deployment.CommitMessage;

            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Deployment {DeploymentId} completed successfully for {App}",
                deploymentId,
                deployment.Application.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Deployment {DeploymentId} failed", deploymentId);

            var deployment = await _context.Deployments.FindAsync(deploymentId);
            if (deployment != null)
            {
                deployment.Status = DeploymentStatus.Failed;
                deployment.FinishedAt = DateTime.UtcNow;
                deployment.ErrorMessage = ex.Message;
                await _context.SaveChangesAsync();
            }
        }
    }

    private bool VerifySignature(string payload, string? signature, string? secret)
    {
        if (string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(secret))
            return false;

        // Remove "sha256=" prefix
        if (!signature.StartsWith("sha256="))
            return false;

        var signatureHash = signature.Substring(7);

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var computedSignature = Convert.ToHexString(hash).ToLower();

        return signatureHash == computedSignature;
    }

    private bool ShouldSkipDeployment(string? commitMessage)
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
