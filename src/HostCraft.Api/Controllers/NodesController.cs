using Microsoft.AspNetCore.Mvc;
using HostCraft.Core.Interfaces;
using HostCraft.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HostCraft.Api.Controllers;

[ApiController]
[Route("api/servers/{serverId}/[controller]")]
public class NodesController : ControllerBase
{
    private readonly HostCraftDbContext _context;
    private readonly IDockerService _dockerService;
    private readonly ILogger<NodesController> _logger;
    
    public NodesController(
        HostCraftDbContext context,
        IDockerService dockerService,
        ILogger<NodesController> logger)
    {
        _context = context;
        _dockerService = dockerService;
        _logger = logger;
    }
    
    /// <summary>
    /// List all swarm nodes on a server
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<NodeDto>>> ListNodes(int serverId)
    {
        var server = await _context.Servers.FindAsync(serverId);
        
        if (server == null)
        {
            return NotFound(new { error = $"Server {serverId} not found" });
        }
        
        if (!server.IsSwarmManager)
        {
            return BadRequest(new { error = "Server is not a swarm manager" });
        }
        
        try
        {
            var nodes = await _dockerService.ListNodesAsync(server);
            
            var nodeDtos = nodes.Select(n => new NodeDto(
                n.Id,
                n.Hostname,
                n.Role,
                n.State,
                n.Availability,
                n.IsLeader,
                n.Address,
                n.NanoCPUs,
                n.MemoryBytes,
                n.EngineVersion,
                n.Platform
            ));
            
            return Ok(nodeDtos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing nodes for server {ServerId}", serverId);
            return StatusCode(500, new { error = "Failed to list nodes", message = ex.Message });
        }
    }
    
    /// <summary>
    /// Get details of a specific node
    /// </summary>
    [HttpGet("{nodeId}")]
    public async Task<ActionResult<NodeDto>> GetNode(int serverId, string nodeId)
    {
        var server = await _context.Servers.FindAsync(serverId);
        
        if (server == null)
        {
            return NotFound(new { error = $"Server {serverId} not found" });
        }
        
        try
        {
            var node = await _dockerService.InspectNodeAsync(server, nodeId);
            
            if (node == null)
            {
                return NotFound(new { error = $"Node {nodeId} not found" });
            }
            
            var nodeDto = new NodeDto(
                node.Id,
                node.Hostname,
                node.Role,
                node.State,
                node.Availability,
                node.IsLeader,
                node.Address,
                node.NanoCPUs,
                node.MemoryBytes,
                node.EngineVersion,
                node.Platform
            );
            
            return Ok(nodeDto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting node {NodeId} for server {ServerId}", nodeId, serverId);
            return StatusCode(500, new { error = "Failed to get node", message = ex.Message });
        }
    }
    
    /// <summary>
    /// Update a node (promote/demote, drain/activate)
    /// </summary>
    [HttpPut("{nodeId}")]
    public async Task<IActionResult> UpdateNode(int serverId, string nodeId, [FromBody] NodeUpdateDto update)
    {
        var server = await _context.Servers.FindAsync(serverId);
        
        if (server == null)
        {
            return NotFound(new { error = $"Server {serverId} not found" });
        }
        
        try
        {
            var request = new NodeUpdateRequest(update.Role, update.Availability);
            var success = await _dockerService.UpdateNodeAsync(server, nodeId, request);
            
            if (success)
            {
                return Ok(new { message = "Node updated successfully" });
            }
            else
            {
                return BadRequest(new { error = "Failed to update node" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating node {NodeId} for server {ServerId}", nodeId, serverId);
            return StatusCode(500, new { error = "Failed to update node", message = ex.Message });
        }
    }
    
    /// <summary>
    /// Remove a node from the swarm
    /// </summary>
    [HttpDelete("{nodeId}")]
    public async Task<IActionResult> RemoveNode(int serverId, string nodeId, [FromQuery] bool force = false)
    {
        var server = await _context.Servers.FindAsync(serverId);
        
        if (server == null)
        {
            return NotFound(new { error = $"Server {serverId} not found" });
        }
        
        try
        {
            var success = await _dockerService.RemoveNodeAsync(server, nodeId, force);
            
            if (success)
            {
                return Ok(new { message = "Node removed successfully" });
            }
            else
            {
                return BadRequest(new { error = "Failed to remove node" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing node {NodeId} for server {ServerId}", nodeId, serverId);
            return StatusCode(500, new { error = "Failed to remove node", message = ex.Message });
        }
    }
}

public record NodeDto(
    string Id,
    string Hostname,
    string Role,
    string State,
    string Availability,
    bool IsLeader,
    string Address,
    long NanoCPUs,
    long MemoryBytes,
    string EngineVersion,
    string Platform);

public record NodeUpdateDto(string? Role, string? Availability);
