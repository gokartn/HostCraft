using Microsoft.AspNetCore.Mvc;
using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using HostCraft.Core.Interfaces;
using HostCraft.Core.Interfaces.Repositories;
using HostCraft.Core.Models;
using HostCraft.Core.Models.Applications.Operations;
using HostCraft.Api.Models.Applications;
using HostCraft.Api.Services;

namespace HostCraft.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ApplicationsController : ControllerBase
{
    private readonly IApplicationsWorkflowService _workflow;
    private readonly IApplicationOperationsService _operationsService;
    private readonly ILogger<ApplicationsController> _logger;
    
    public ApplicationsController(
        IApplicationsWorkflowService workflow,
        IApplicationOperationsService operationsService,
        ILogger<ApplicationsController> logger)
    {
        _workflow = workflow;
        _operationsService = operationsService;
        _logger = logger;
    }
    
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ApplicationDto>>> GetApplications(
        [FromQuery] int? serverId = null,
        [FromQuery] int? projectId = null,
        [FromQuery] bool paged = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        if (paged)
        {
            var pagedResult = await _workflow.GetApplicationsPagedAsync(serverId, projectId, Math.Max(1, page), Math.Clamp(pageSize, 1, 200), HttpContext.RequestAborted);
            return ToActionResult(pagedResult);
        }

        var result = await _workflow.GetApplicationsAsync(serverId, projectId, HttpContext.RequestAborted);
        return ToActionResult(result);
    }
    
    [HttpGet("{id}")]
    public async Task<ActionResult<ApplicationWithDeploymentsDto>> GetApplication(int id)
    {
        var result = await _workflow.GetApplicationAsync(id, HttpContext.RequestAborted);
        return ToActionResult(result);
    }

    [HttpGet("servers/{serverId}")]
    public async Task<ActionResult<IEnumerable<ServerResponseDto>>> GetServers()
    {
        var result = await _workflow.GetServersAsync(HttpContext.RequestAborted);
        return ToActionResult(result);
    }
    
    [HttpGet("projects")]
    public async Task<ActionResult<IEnumerable<ProjectDto>>> GetProjects()
    {
        var result = await _workflow.GetProjectsAsync(HttpContext.RequestAborted);
        return ToActionResult(result);
    }
    
    [HttpPost]
    public async Task<ActionResult<ApplicationDto>> CreateApplication(CreateApplicationRequest request)
    {
        var result = await _workflow.CreateApplicationAsync(request, HttpContext.RequestAborted);
        return ToActionResult(result, nameof(GetApplication));
    }
    
    [HttpPost("{id}/scale")]
    public async Task<ActionResult> ScaleApplication(int id, [FromQuery] int replicas)
    {
        var result = await _workflow.ScaleApplicationAsync(id, replicas, HttpContext.RequestAborted);
        return ToActionResult(result);
    }
    
    [HttpPost("{id}/deploy")]
    public async Task<ActionResult> RedeployApplication(int id)
    {
        var result = await _workflow.RedeployAsync(id, HttpContext.RequestAborted);
        return ToActionResult(result);
    }
    
    [HttpGet("{id}/logs")]
    public async Task<ActionResult> GetApplicationLogs(int id)
    {
        var result = await _workflow.GetApplicationLogsAsync(id, HttpContext.RequestAborted);
        if (!result.Success)
        {
            return ToActionResult(result);
        }

        return Content(result.Data!, "text/plain");
    }

    [HttpGet("{id}/tasks")]
    public async Task<ActionResult<IReadOnlyList<ServiceTaskContainerRef>>> GetServiceTasks(int id)
    {
        var result = await _operationsService.GetServiceTasksAsync(id, HttpContext.RequestAborted);
        
        if (!result.Success)
        {
            return StatusCode(StatusCodes.Status400BadRequest, new { error = result.ErrorMessage });
        }
        
        return Ok(result.Data);
    }

    [HttpGet("{id}/tasks/{taskId}/logs")]
    public async Task<ActionResult> GetTaskLogs(int id, string taskId)
    {
        var result = await _operationsService.GetTaskLogsAsync(id, taskId, HttpContext.RequestAborted);
        
        if (!result.Success)
        {
            return StatusCode(StatusCodes.Status400BadRequest, new { error = result.ErrorMessage });
        }

        using var reader = new StreamReader(result.Data!, leaveOpen: false);
        var content = await reader.ReadToEndAsync();
        return Content(content, "text/plain");
    }

    [HttpGet("{id}/traefik/preview")]
    public async Task<ActionResult<TraefikPreviewResponse>> GetTraefikPreview(int id)
    {
        var result = await _workflow.GetTraefikPreviewAsync(id, HttpContext.RequestAborted);
        return ToActionResult(result);
    }

    [HttpPost("{id}/traefik/preview")]
    public async Task<ActionResult<TraefikPreviewResponse>> PreviewTraefikOverrides(int id, [FromBody] TraefikOverridesRequest request)
    {
        var result = await _workflow.PreviewTraefikOverridesAsync(id, request, HttpContext.RequestAborted);
        return ToActionResult(result);
    }

    [HttpPut("{id}/traefik/overrides")]
    public async Task<ActionResult> UpdateTraefikOverrides(int id, [FromBody] TraefikOverridesRequest request)
    {
        var result = await _workflow.UpdateTraefikOverridesAsync(id, request, HttpContext.RequestAborted);
        return ToActionResult(result);
    }

    /// <summary>
    /// Update application configuration including domain settings.
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<ApplicationWithDeploymentsDto>> UpdateApplication(int id, [FromBody] UpdateApplicationRequest request)
    {
        var result = await _workflow.UpdateApplicationAsync(id, request, HttpContext.RequestAborted);
        return ToActionResult(result);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteApplication(int id)
    {
        var result = await _workflow.DeleteApplicationAsync(id, HttpContext.RequestAborted);
        return ToActionResult(result);
    }

    /// <summary>
    /// Deploy a Docker Compose application
    /// </summary>
    [HttpPost("compose")]
    public async Task<ActionResult<ApplicationDto>> DeployCompose([FromBody] HostCraft.Core.Models.DeployComposeRequest request)
    {
        var result = await _workflow.DeployComposeAsync(request, HttpContext.RequestAborted);
        return ToActionResult(result, nameof(GetApplication));
    }

    /// <summary>
    /// Validate Docker Compose YAML without deploying
    /// </summary>
    [HttpPost("compose/validate")]
    public async Task<ActionResult> ValidateCompose([FromBody] HostCraft.Core.Models.ValidateComposeRequest request)
    {
        var result = await _workflow.ValidateComposeAsync(request, HttpContext.RequestAborted);
        return ToActionResult(result);
    }

    /// <summary>
    /// List all Docker Stacks across swarm managers
    /// </summary>
    [HttpGet("stacks")]
    public async Task<ActionResult> ListStacks([FromQuery] int? serverId = null)
    {
        var result = await _workflow.ListStacksAsync(serverId, HttpContext.RequestAborted);
        return ToActionResult(result);
    }

    /// <summary>
    /// Remove a Docker Stack
    /// </summary>
    [HttpDelete("{id}/stack")]
    public async Task<IActionResult> RemoveStack(int id)
    {
        var result = await _workflow.RemoveStackAsync(id, HttpContext.RequestAborted);
        return ToActionResult(result);
    }
    
    [HttpGet("{id}/status")]
    public async Task<ActionResult<ApplicationStatusDto>> GetApplicationStatus(int id)
    {
        var result = await _workflow.GetApplicationStatusAsync(id, HttpContext.RequestAborted);
        return ToActionResult(result);
    }

    [HttpGet("{id}/metrics")]
    public async Task<ActionResult<ApplicationMetricsDto>> GetApplicationMetrics(int id)
    {
        var result = await _workflow.GetApplicationMetricsAsync(id, HttpContext.RequestAborted);
        return ToActionResult(result);
    }
    
    [HttpGet("orphans")]
    public async Task<ActionResult<OrphanedResourcesDto>> GetOrphanedResources([FromQuery] int? serverId = null)
    {
        var result = await _workflow.GetOrphanedResourcesAsync(serverId, HttpContext.RequestAborted);
        return ToActionResult(result);
    }
    
    [HttpPost("orphans/{containerId}/cleanup")]
    public async Task<IActionResult> CleanupOrphanedContainer(string containerId, [FromQuery] int serverId)
    {
        var result = await _workflow.CleanupOrphanedContainerAsync(containerId, serverId, HttpContext.RequestAborted);
        return ToActionResult(result);
    }
    
    [HttpPost("orphans/services/{serviceId}/cleanup")]
    public async Task<IActionResult> CleanupOrphanedService(string serviceId, [FromQuery] int serverId)
    {
        var result = await _workflow.CleanupOrphanedServiceAsync(serviceId, serverId, HttpContext.RequestAborted);
        return ToActionResult(result);
    }

    private ActionResult ToActionResult(ApiActionResult result)
    {
        if (result.Success)
        {
            if (result.StatusCode == StatusCodes.Status204NoContent)
            {
                return StatusCode(StatusCodes.Status204NoContent);
            }

            return StatusCode(result.StatusCode);
        }

        return StatusCode(result.StatusCode, new { error = result.Error ?? "Request failed" });
    }

    private ActionResult ToActionResult(ApiActionResult result, string actionName)
    {
        if (result.Success)
        {
            if (result.StatusCode == StatusCodes.Status201Created)
            {
                // Expect caller to have set location through CreatedAtAction in workflow; return generic 201.
                return StatusCode(StatusCodes.Status201Created);
            }

            return StatusCode(result.StatusCode);
        }

        return StatusCode(result.StatusCode, new { error = result.Error ?? "Request failed" });
    }

    private ActionResult ToActionResult<T>(ApiActionResult<T> result)
    {
        if (result.Success)
        {
            if (result.StatusCode == StatusCodes.Status204NoContent)
            {
                return StatusCode(StatusCodes.Status204NoContent);
            }

            return StatusCode(result.StatusCode, result.Data);
        }

        return StatusCode(result.StatusCode, new { error = result.Error ?? "Request failed" });
    }

    private ActionResult ToActionResult<T>(ApiActionResult<T> result, string actionName)
    {
        if (result.Success)
        {
            if (result.StatusCode == StatusCodes.Status201Created)
            {
                return CreatedAtAction(actionName, new { id = (result.Data as ApplicationDto)?.Id }, result.Data);
            }

            if (result.StatusCode == StatusCodes.Status204NoContent)
            {
                return StatusCode(StatusCodes.Status204NoContent);
            }

            return StatusCode(result.StatusCode, result.Data);
        }

        return StatusCode(result.StatusCode, new { error = result.Error ?? "Request failed" });
    }
}

