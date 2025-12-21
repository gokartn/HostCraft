using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HostCraft.Core.Enums;
using HostCraft.Infrastructure.Persistence;

namespace HostCraft.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DeploymentsController : ControllerBase
{
    private readonly HostCraftDbContext _context;
    private readonly ILogger<DeploymentsController> _logger;
    
    public DeploymentsController(HostCraftDbContext context, ILogger<DeploymentsController> logger)
    {
        _context = context;
        _logger = logger;
    }
    
    [HttpGet]
    public async Task<ActionResult<IEnumerable<DeploymentListDto>>> GetDeployments(
        [FromQuery] int? applicationId = null,
        [FromQuery] DeploymentStatus? status = null,
        [FromQuery] int limit = 100)
    {
        var query = _context.Deployments
            .Include(d => d.Application)
            .ThenInclude(a => a.Server)
            .AsQueryable();
        
        if (applicationId.HasValue)
            query = query.Where(d => d.ApplicationId == applicationId.Value);
        
        if (status.HasValue)
            query = query.Where(d => d.Status == status.Value);
        
        var deployments = await query
            .OrderByDescending(d => d.StartedAt)
            .Take(limit)
            .ToListAsync();
        
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
            ErrorMessage = d.ErrorMessage
        }));
    }
    
    [HttpGet("{id}")]
    public async Task<ActionResult<DeploymentDetailResponseDto>> GetDeployment(int id)
    {
        var deployment = await _context.Deployments
            .Include(d => d.Application)
            .ThenInclude(a => a.Server)
            .Include(d => d.Logs.OrderBy(l => l.Timestamp))
            .FirstOrDefaultAsync(d => d.Id == id);
        
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
}

public record DeploymentListDto
{
    public int Id { get; init; }
    public int ApplicationId { get; init; }
    public string ApplicationName { get; init; } = string.Empty;
    public string ServerName { get; init; } = string.Empty;
    public DeploymentStatus Status { get; init; }
    public string? ContainerId { get; init; }
    public string? ServiceId { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? FinishedAt { get; init; }
    public string? ErrorMessage { get; init; }
}

public record DeploymentDetailResponseDto
{
    public int Id { get; init; }
    public int ApplicationId { get; init; }
    public string ApplicationName { get; init; } = string.Empty;
    public string ServerName { get; init; } = string.Empty;
    public DeploymentStatus Status { get; init; }
    public string? ContainerId { get; init; }
    public string? ServiceId { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? FinishedAt { get; init; }
    public string? ErrorMessage { get; init; }
    public List<DeploymentLogResponseDto> Logs { get; init; } = new();
}

public record DeploymentLogResponseDto
{
    public int Id { get; init; }
    public string Message { get; init; } = string.Empty;
    public string LogLevel { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; }
}
