using Microsoft.AspNetCore.Mvc;
using HostCraft.Core.Interfaces;
using HostCraft.Infrastructure.Persistence;

namespace HostCraft.Api.Controllers;

[ApiController]
[Route("api/servers/{serverId}/[controller]")]
public class ImagesController : ControllerBase
{
    private readonly HostCraftDbContext _context;
    private readonly IDockerService _dockerService;
    private readonly ILogger<ImagesController> _logger;
    
    public ImagesController(
        HostCraftDbContext context,
        IDockerService dockerService,
        ILogger<ImagesController> logger)
    {
        _context = context;
        _dockerService = dockerService;
        _logger = logger;
    }
    
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ImageInfo>>> ListImages(int serverId)
    {
        var server = await _context.Servers.FindAsync(serverId);
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
        var server = await _context.Servers.FindAsync(serverId);
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

public record PullImageRequest
{
    public string ImageName { get; init; } = string.Empty;
}
