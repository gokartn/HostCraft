using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
    public async Task<ActionResult> TriggerUpdate([FromBody] TriggerUpdateRequest request)
    {
        try
        {
            var success = await _updateService.TriggerUpdateAsync(request.Version);
            
            if (success)
            {
                return Ok(new { message = "Update triggered successfully" });
            }
            
            return BadRequest(new { error = "Update trigger not implemented or failed" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to trigger update");
            return StatusCode(500, new { error = "Failed to trigger update" });
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

public record TriggerUpdateRequest(string Version);

public class VersionInfo
{
    public string Version { get; set; } = "";
    public DateTime BuildDate { get; set; }
}
