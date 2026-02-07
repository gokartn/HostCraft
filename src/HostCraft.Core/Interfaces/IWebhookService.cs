using HostCraft.Core.Entities;
using System.Text.Json;

namespace HostCraft.Core.Interfaces;

/// <summary>
/// Service for processing GitHub webhook events.
/// </summary>
public interface IWebhookService
{
    /// <summary>
    /// Verifies GitHub webhook signature.
    /// </summary>
    bool VerifyGitHubSignature(string body, string? signature, string? secret);

    /// <summary>
    /// Handles GitHub push event and creates deployment if needed.
    /// </summary>
    Task<WebhookProcessingResult> HandlePushEventAsync(Application application, JsonElement payload, CancellationToken cancellationToken = default);

    /// <summary>
    /// Handles GitHub pull request event and creates preview deployment if needed.
    /// </summary>
    Task<WebhookProcessingResult> HandlePullRequestEventAsync(Application application, JsonElement payload, CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines if deployment should be skipped based on commit message.
    /// </summary>
    bool ShouldSkipDeployment(string? commitMessage);
}

/// <summary>
/// Result of webhook event processing.
/// </summary>
public record WebhookProcessingResult(
    bool Success,
    string Message,
    int? DeploymentId = null,
    string? CommitSha = null,
    string? PreviewUrl = null);
