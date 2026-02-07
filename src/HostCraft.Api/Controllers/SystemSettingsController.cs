using HostCraft.Api.Models.SystemSettings;
using HostCraft.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HostCraft.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SystemSettingsController : ControllerBase
{
    private readonly ISystemSettingsWorkflowService _systemSettingsWorkflow;

    public SystemSettingsController(
        ISystemSettingsWorkflowService systemSettingsWorkflow)
    {
        _systemSettingsWorkflow = systemSettingsWorkflow;
    }

    /// <summary>
    /// Get current HostCraft system settings
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<SystemSettingsDto>> GetSettings()
    {
        var result = await _systemSettingsWorkflow.GetSettingsAsync(HttpContext.RequestAborted);
        return ToActionResult(result);
    }

    /// <summary>
    /// Update HostCraft domain and SSL configuration
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ConfigureHostCraftResponse>> ConfigureHostCraft(
        [FromBody] ConfigureHostCraftRequest request)
    {
        var result = await _systemSettingsWorkflow.ConfigureHostCraftAsync(request, HttpContext.RequestAborted);
        return ToActionResult(result);
    }

    /// <summary>
    /// Configure Traefik dashboard domain and authentication
    /// </summary>
    [HttpPost("traefik-dashboard")]
    public async Task<ActionResult<ConfigureTraefikDashboardResponse>> ConfigureTraefikDashboard(
        [FromBody] ConfigureTraefikDashboardRequest request)
    {
        var result = await _systemSettingsWorkflow.ConfigureTraefikDashboardAsync(request, HttpContext.RequestAborted);
        return ToActionResult(result);
    }

    /// <summary>
    /// Get container logs from HostCraft services (Developer Mode)
    /// </summary>
    [HttpGet("logs")]
    public async Task<ActionResult<ContainerLogsResponse>> GetContainerLogs([FromQuery] int lines = 200)
    {
        var result = await _systemSettingsWorkflow.GetContainerLogsAsync(lines, HttpContext.RequestAborted);
        return ToActionResult(result);
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
}
