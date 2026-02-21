using HostCraft.Api.Models.Deployments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HostCraft.Core.Enums;
using HostCraft.Core.Interfaces.Repositories;

namespace HostCraft.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DeploymentsController : ControllerBase
{
    private readonly IDeploymentRepository _deploymentRepository;
    private readonly ILogger<DeploymentsController> _logger;
    
    public DeploymentsController(IDeploymentRepository deploymentRepository, ILogger<DeploymentsController> logger)
    {
        _deploymentRepository = deploymentRepository;
        _logger = logger;
    }
    
    [HttpGet]
    public async Task<ActionResult<IEnumerable<DeploymentListDto>>> GetDeployments(
        [FromQuery] int? applicationId = null,
        [FromQuery] DeploymentStatus? status = null,
        [FromQuery] int limit = 100)
    {
        var deployments = await _deploymentRepository.GetDeploymentsAsync(applicationId, status, limit);
        
        return Ok(deployments.Select(d => new DeploymentListDto
        {
            Id = d.Id,
            ApplicationId = d.ApplicationId,
            ApplicationName = d.Application.Name,
            ServerName = d.Application.Server.Name,
            Status = d.Status,
            ContainerId = d.ContainerId,
            ServiceId = d.ServiceId,
            StartedAt = d.StartedAt,
            FinishedAt = d.FinishedAt,
            ErrorMessage = d.ErrorMessage,
            TriggeredBy = d.TriggeredBy,
            CommitSha = d.CommitHash,
            CommitMessage = d.CommitMessage,
            CommitAuthor = d.CommitAuthor
        }));
    }
    
    [HttpGet("{id}")]
    public async Task<ActionResult<DeploymentDetailResponseDto>> GetDeployment(int id)
    {
        var deployment = await _deploymentRepository.GetByIdWithApplicationAndLogsAsync(id);

        if (deployment == null)
            return NotFound();

        return new DeploymentDetailResponseDto
        {
            Id = deployment.Id,
            ApplicationId = deployment.ApplicationId,
            ApplicationName = deployment.Application.Name,
            ServerName = deployment.Application.Server.Name,
            Status = deployment.Status,
            ContainerId = deployment.ContainerId,
            ServiceId = deployment.ServiceId,
            StartedAt = deployment.StartedAt,
            FinishedAt = deployment.FinishedAt,
            ErrorMessage = deployment.ErrorMessage,
            Logs = deployment.Logs.Select(l => new DeploymentLogResponseDto
            {
                Id = l.Id,
                Message = l.Message,
                LogLevel = l.Level,
                Timestamp = l.Timestamp
            }).ToList()
        };
    }

    /// <summary>
    /// Get deployment logs with optional filtering by last seen ID (for polling).
    /// </summary>
    [HttpGet("{id}/logs")]
    public async Task<ActionResult<IEnumerable<DeploymentLogResponseDto>>> GetDeploymentLogs(
        int id,
        [FromQuery] int afterId = 0)
    {
        var deployment = await _deploymentRepository.GetByIdAsync(id);
        if (deployment == null)
            return NotFound();

        var logs = await _deploymentRepository.GetLogsAfterAsync(id, afterId);

        var response = logs.Select(l => new DeploymentLogResponseDto
        {
            Id = l.Id,
            Message = l.Message,
            LogLevel = l.Level,
            Timestamp = l.Timestamp
        });

        return Ok(response);
    }

    /// <summary>
    /// Get deployment status (for polling during build).
    /// </summary>
    [HttpGet("{id}/status")]
    public async Task<ActionResult<DeploymentStatusDto>> GetDeploymentStatus(int id)
    {
        var deployment = await _deploymentRepository.GetByIdAsync(id);
        if (deployment == null)
            return NotFound();

        return new DeploymentStatusDto
        {
            Id = deployment.Id,
            Status = deployment.Status.ToString(),
            StartedAt = deployment.StartedAt,
            FinishedAt = deployment.FinishedAt,
            ErrorMessage = deployment.ErrorMessage
        };
    }
}
