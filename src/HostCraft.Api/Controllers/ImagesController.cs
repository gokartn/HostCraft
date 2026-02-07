using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HostCraft.Api.Models.Images;
using HostCraft.Core.Interfaces.Repositories;
using HostCraft.Core.Interfaces;

namespace HostCraft.Api.Controllers;

[ApiController]
[Route("api/servers/{serverId}/[controller]")]
[Authorize]
public class ImagesController : ControllerBase
{
    private readonly IServerRepository _serverRepository;
    private readonly IDockerService _dockerService;
    private readonly ILogger<ImagesController> _logger;
    
    public ImagesController(
        IServerRepository serverRepository,
        IDockerService dockerService,
        ILogger<ImagesController> logger)
    {
        _serverRepository = serverRepository;
        _dockerService = dockerService;
        _logger = logger;
    }
    
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ImageInfo>>> ListImages(int serverId)
    {
        var server = await _serverRepository.GetByIdWithPrivateKeyAsync(serverId);
        if (server == null)
            return NotFound(new { error = "Server not found" });
        
        try
        {
            var images = await _dockerService.ListImagesAsync(server);
            return Ok(images);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing images on server {ServerId}", serverId);
            return StatusCode(500, new { error = ex.Message });
        }
    }
    
    [HttpPost("pull")]
    public async Task<IActionResult> PullImage(
        int serverId,
        [FromBody] PullImageRequest request)
    {
        var server = await _serverRepository.GetByIdWithPrivateKeyAsync(serverId);
        if (server == null)
            return NotFound(new { error = "Server not found" });
        
        try
        {
            var progress = new Progress<string>(msg =>
            {
                _logger.LogInformation("Pull progress: {Message}", msg);
            });
            
            await _dockerService.PullImageAsync(server, request.ImageName, progress);
            return Ok(new { message = $"Image {request.ImageName} pulled successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pulling image {ImageName}", request.ImageName);
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
