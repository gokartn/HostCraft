using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using HostCraft.Core.Interfaces;
using HostCraft.Core.Interfaces.Repositories;
using HostCraft.Api.Models.Servers;
using HostCraft.Api.Services;
using System.Runtime.InteropServices;

namespace HostCraft.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ServersController : ControllerBase
{
    private readonly IServersWorkflowService _workflow;
    private readonly ILogger<ServersController> _logger;
    
    public ServersController(
        IServersWorkflowService workflow,
        ILogger<ServersController> logger)
    {
        _workflow = workflow;
        _logger = logger;
    }
    
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ServerListDto>>> GetServers(
        [FromQuery] bool paged = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var result = await _workflow.GetServersAsync(paged, Math.Max(1, page), Math.Clamp(pageSize, 1, 200), HttpContext.RequestAborted);
        return ToActionResult(result);
    }
    
    [HttpGet("{id}")]
    public async Task<ActionResult<Server>> GetServer(int id)
    {
        var result = await _workflow.GetServerAsync(id, HttpContext.RequestAborted);
        return ToActionResult(result);
    }
    
    [HttpPost]
    public async Task<ActionResult<Server>> CreateServer(CreateServerRequest request)
    {
        var result = await _workflow.CreateServerAsync(request, HttpContext.RequestAborted);
        return ToActionResult(result, nameof(GetServer));
    }
    
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateServer(int id, UpdateServerRequest request)
    {
        var result = await _workflow.UpdateServerAsync(id, request, HttpContext.RequestAborted);
        return ToActionResult(result);
    }
    
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteServer(int id)
    {
        var result = await _workflow.DeleteServerAsync(id, HttpContext.RequestAborted);
        return ToActionResult(result);
    }
    
    [HttpPost("configure-localhost")]
    public async Task<ActionResult<Server>> ConfigureLocalhostServer()
    {
        var result = await _workflow.ConfigureLocalhostAsync(HttpContext.RequestAborted);
        return ToActionResult(result);
    }
    
    [HttpPost("validate")]
    public async Task<ActionResult<ServerValidationResult>> ValidateServerConnection(CreateServerRequest request)
    {
        var result = await _workflow.ValidateNewServerAsync(request, HttpContext.RequestAborted);
        return ToActionResult(result);
    }
    
    [HttpPost("{id}/validate")]
    public async Task<ActionResult<ServerValidationResult>> ValidateExistingServer(int id)
    {
        var result = await _workflow.ValidateExistingServerAsync(id, HttpContext.RequestAborted);
        return ToActionResult(result);
    }
    
    [HttpGet("{id}/containers")]
    public async Task<ActionResult<IEnumerable<ContainerInfo>>> GetContainers(int id)
    {
        var result = await _workflow.GetContainersAsync(id, HttpContext.RequestAborted);
        return ToActionResult(result);
    }
    
    [HttpGet("{id}/services")]
    public async Task<ActionResult<IEnumerable<ServiceInfo>>> GetServices(int id)
    {
        var result = await _workflow.GetServicesAsync(id, HttpContext.RequestAborted);
        return ToActionResult(result);
    }

    [HttpGet("{id}/public-key")]
    public async Task<ActionResult<object>> GetPublicKey(int id)
    {
        var result = await _workflow.GetPublicKeyAsync(id, HttpContext.RequestAborted);
        return ToActionResult(result);
    }
    
    /// <summary>
    /// Refresh server's swarm status detection. Auto-rejoins workers that lost swarm connection.
    /// </summary>
    [HttpPost("{id}/refresh-swarm-status")]
    public async Task<IActionResult> RefreshSwarmStatus(int id)
    {
        var result = await _workflow.RefreshSwarmStatusAsync(id, HttpContext.RequestAborted);
        return ToActionResult(result);
    }
    
    /// <summary>
    /// Initialize Docker Swarm on a server
    /// </summary>
    [HttpPost("{id}/swarm/init")]
    public async Task<IActionResult> InitializeSwarm(int id)
    {
        var result = await _workflow.InitializeSwarmAsync(id, HttpContext.RequestAborted);
        return ToActionResult(result);
    }
    
    /// <summary>
    /// Get swarm join tokens
    /// </summary>
    [HttpGet("{id}/swarm/tokens")]
    public async Task<ActionResult> GetJoinTokens(int id)
    {
        var result = await _workflow.GetJoinTokensAsync(id, HttpContext.RequestAborted);
        return ToActionResult(result);
    }
    
    /// <summary>
    /// Join a standalone server to an existing swarm as a manager node
    /// </summary>
    [HttpPost("{existingManagerId}/swarm/join-manager")]
    public async Task<ActionResult> JoinAsManager(int existingManagerId, [FromBody] JoinManagerRequest request)
    {
        var result = await _workflow.JoinAsManagerAsync(existingManagerId, request, HttpContext.RequestAborted);
        return ToActionResult(result);
    }
    
    /// <summary>
    /// Promote a swarm worker node to manager
    /// </summary>
    [HttpPost("{id}/swarm/promote-to-manager")]
    public async Task<ActionResult> PromoteToManager(int id)
    {
        var result = await _workflow.PromoteToManagerAsync(id, HttpContext.RequestAborted);
        return ToActionResult(result);
    }
    
    /// <summary>
    /// Auto-configure server by installing Docker and prerequisites
    /// </summary>
    [HttpPost("{id}/auto-configure")]
    public async Task<ActionResult> AutoConfigureServer(int id)
    {
        var result = await _workflow.AutoConfigureServerAsync(id, HttpContext.RequestAborted);
        return ToActionResult(result);
    }
    
    /// <summary>
    /// Get system information including swarm status
    /// </summary>
    [HttpGet("{id}/info")]
    public async Task<ActionResult<SystemInfo>> GetSystemInfo(int id)
    {
        var result = await _workflow.GetSystemInfoAsync(id, HttpContext.RequestAborted);
        return ToActionResult(result);
    }

    /// <summary>
    /// Update wizard step for server during HA setup
    /// </summary>
    [HttpPatch("{id}/wizard-step")]
    public async Task<ActionResult> UpdateWizardStep(int id, [FromBody] WizardStepUpdate request)
    {
        var result = await _workflow.UpdateWizardStepAsync(id, request, HttpContext.RequestAborted);
        return ToActionResult(result);
    }

    /// <summary>
    /// Mark wizard as complete for server
    /// </summary>
    [HttpPost("{id}/wizard-complete")]
    public async Task<ActionResult> CompleteWizard(int id)
    {
        var result = await _workflow.CompleteWizardAsync(id, HttpContext.RequestAborted);
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
                return StatusCode(StatusCodes.Status201Created);
            }

            if (result.StatusCode == StatusCodes.Status204NoContent)
            {
                return StatusCode(StatusCodes.Status204NoContent);
            }

            return StatusCode(result.StatusCode);
        }

        return StatusCode(result.StatusCode, new { error = result.Error ?? "Request failed" });
    }

    private ActionResult<T> ToActionResult<T>(ApiActionResult<T> result)
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

    private ActionResult<T> ToActionResult<T>(ApiActionResult<T> result, string actionName)
    {
        if (result.Success)
        {
            if (result.StatusCode == StatusCodes.Status201Created)
            {
                return CreatedAtAction(actionName, new { id = (result.Data as Server)?.Id }, result.Data);
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
