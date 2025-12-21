using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HostCraft.Core.Interfaces;
using HostCraft.Infrastructure.Persistence;

namespace HostCraft.Api.Controllers;

[ApiController]
[Route("api/servers/{serverId}/[controller]")]
[Authorize]
public class NetworksController : ControllerBase
{
    private readonly HostCraftDbContext _context;
    private readonly IDockerService _dockerService;
    private readonly ILogger<NetworksController> _logger;
    
    public NetworksController(
        HostCraftDbContext context,
        IDockerService dockerService,
        ILogger<NetworksController> logger)
    {
        _context = context;
        _dockerService = dockerService;
        _logger = logger;
    }
    
    [HttpGet]
    public async Task<ActionResult<IEnumerable<NetworkInfo>>> ListNetworks(int serverId)
    {
        var server = await _context.Servers
            .Include(s => s.PrivateKey)
            .FirstOrDefaultAsync(s => s.Id == serverId);
        if (server == null)
            return NotFound(new { error = "Server not found" });
        
        try
        {
            var networks = await _dockerService.ListNetworksAsync(server);
            return Ok(networks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing networks on server {ServerId}", serverId);
            return StatusCode(500, new { error = ex.Message });
        }
    }
    
    [HttpPost]
    public async Task<ActionResult<CreateNetworkResponse>> CreateNetwork(
        int serverId,
        [FromBody] CreateNetworkRequest request)
    {
        var server = await _context.Servers
            .Include(s => s.PrivateKey)
            .FirstOrDefaultAsync(s => s.Id == serverId);
        if (server == null)
            return NotFound(new { error = "Server not found" });
        
        try
        {
            var networkId = await _dockerService.CreateNetworkAsync(server, request);
            return Ok(new CreateNetworkResponse { Id = networkId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating network on server {ServerId}", serverId);
            return StatusCode(500, new { error = ex.Message });
        }
    }
    
    [HttpDelete("{networkId}")]
    public async Task<IActionResult> RemoveNetwork(int serverId, string networkId)
    {
        var server = await _context.Servers
            .Include(s => s.PrivateKey)
            .FirstOrDefaultAsync(s => s.Id == serverId);
        if (server == null)
            return NotFound(new { error = "Server not found" });
        
        try
        {
            await _dockerService.RemoveNetworkAsync(server, networkId);
            return Ok(new { message = "Network removed" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing network {NetworkId}", networkId);
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

public record CreateNetworkResponse
{
    public string Id { get; init; } = string.Empty;
}
