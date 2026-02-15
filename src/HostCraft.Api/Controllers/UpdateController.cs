using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HostCraft.Api.Models.Update;
using HostCraft.Core.Interfaces;

namespace HostCraft.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UpdateController : ControllerBase
{
    private readonly IUpdateService _updateService;
    private readonly ILogger<UpdateController> _logger;

    public UpdateController(IUpdateService updateService, ILogger<UpdateController> logger)
    {
        _updateService = updateService;
        _logger = logger;
    }

    [HttpGet("check")]
    public async Task<ActionResult<UpdateInfo>> CheckForUpdates()
    {
        try
        {
            var updateInfo = await _updateService.CheckForUpdatesAsync();
            return Ok(updateInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check for updates");
            return StatusCode(500, new { error = "Failed to check for updates" });
        }
    }

    [HttpGet("version")]
    public ActionResult<VersionInfo> GetVersion()
    {
        try
        {
            var version = _updateService.GetCurrentVersion();
            return Ok(new VersionInfo
            {
                Version = version,
                BuildDate = GetBuildDate()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get version");
            return StatusCode(500, new { error = "Failed to get version" });
        }
    }

    [HttpPost("trigger")]
    public async Task<ActionResult<UpdateTriggerResult>> TriggerUpdate([FromBody] TriggerUpdateRequest request)
    {
        try
        {
            _logger.LogInformation("Update to version {Version} requested", request.Version);
            var result = await _updateService.TriggerUpdateAsync(request.Version);

            if (result.Success)
            {
                return Ok(result);
            }

            return BadRequest(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to trigger update");
            return StatusCode(500, new UpdateTriggerResult
            {
                Success = false,
                Message = $"Failed to trigger update: {ex.Message}"
            });
        }
    }

    [HttpGet("progress")]
    public ActionResult<UpdateProgress> GetUpdateProgress()
    {
        try
        {
            var progress = _updateService.GetUpdateProgress();
            return Ok(progress);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get update progress");
            return StatusCode(500, new { error = "Failed to get update progress" });
        }
    }

    private DateTime GetBuildDate()
    {
        try
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var fileInfo = new FileInfo(assembly.Location);
            return fileInfo.LastWriteTime;
        }
        catch
        {
            return DateTime.UtcNow;
        }
    }
}
