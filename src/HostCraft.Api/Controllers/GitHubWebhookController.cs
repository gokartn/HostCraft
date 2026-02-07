using System.Text.Json;
using HostCraft.Core.Interfaces;
using HostCraft.Core.Interfaces.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HostCraft.Api.Controllers;

/// <summary>
/// Handles GitHub webhook events for automated deployments.
/// </summary>
[ApiController]
[Route("api/webhooks/github")]
public class GitHubWebhookController : ControllerBase
{
    private readonly IApplicationRepository _applicationRepository;
    private readonly IWebhookService _webhookService;
    private readonly ILogger<GitHubWebhookController> _logger;

    public GitHubWebhookController(
        IApplicationRepository applicationRepository,
        IWebhookService webhookService,
        ILogger<GitHubWebhookController> logger)
    {
        _applicationRepository = applicationRepository;
        _webhookService = webhookService;
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
            var application = await _applicationRepository.GetByUuidWithGitProviderAndServerAsync(applicationUuid);

            if (application == null)
            {
                _logger.LogWarning("Webhook received for unknown application {Uuid}", applicationUuid);
                return NotFound();
            }

            // Verify webhook signature
            var signature = Request.Headers["X-Hub-Signature-256"].FirstOrDefault();
            if (!_webhookService.VerifyGitHubSignature(body, signature, application.WebhookSecret))
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
                var result = await _webhookService.HandlePushEventAsync(application, payload, HttpContext.RequestAborted);
                
                if (result.Success)
                {
                    return Accepted(new
                    {
                        message = result.Message,
                        deploymentId = result.DeploymentId,
                        commit = result.CommitSha,
                        application = application.Name
                    });
                }
                
                return Ok(new { message = result.Message });
            }

            // Handle pull request events
            if (eventType == "pull_request")
            {
                var result = await _webhookService.HandlePullRequestEventAsync(application, payload, HttpContext.RequestAborted);
                
                if (result.Success)
                {
                    if (result.PreviewUrl != null)
                    {
                        return Accepted(new
                        {
                            message = result.Message,
                            deploymentId = result.DeploymentId,
                            previewUrl = result.PreviewUrl
                        });
                    }
                    
                    return Ok(new { message = result.Message });
                }
                
                return Ok(new { message = result.Message });
            }

            return Ok(new { message = $"Event {eventType} received but not processed" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing webhook for {Uuid}", applicationUuid);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
}
