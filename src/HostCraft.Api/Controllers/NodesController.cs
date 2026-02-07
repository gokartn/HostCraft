using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HostCraft.Core.Interfaces;
using HostCraft.Api.Models.Nodes;
using HostCraft.Core.Enums;
using HostCraft.Core.Interfaces.Repositories;

namespace HostCraft.Api.Controllers;

[ApiController]
[Route("api/servers/{serverId}/[controller]")]
[Authorize]
public class NodesController : ControllerBase
{
    private readonly IDockerService _dockerService;
    private readonly ILogger<NodesController> _logger;
    private readonly IServerRepository _serverRepository;
    
    public NodesController(
        IServerRepository serverRepository,
        IDockerService dockerService,
        ILogger<NodesController> logger)
    {
        _serverRepository = serverRepository;
        _dockerService = dockerService;
        _logger = logger;
    }
    
    /// <summary>
    /// List all swarm nodes on a server
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<NodeDto>>> ListNodes(int serverId)
    {
        var server = await _serverRepository.GetByIdWithPrivateKeyAsync(serverId);
        
        if (server == null)
        {
            return NotFound(new { error = $"Server {serverId} not found" });
        }
        
        if (server.Type != ServerType.SwarmManager)
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
        var server = await _serverRepository.GetByIdWithPrivateKeyAsync(serverId);
        
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
        var server = await _serverRepository.GetByIdWithPrivateKeyAsync(serverId);
        
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
        var server = await _serverRepository.GetByIdWithPrivateKeyAsync(serverId);

        if (server == null)
        {
            return NotFound(new { error = $"Server {serverId} not found" });
        }

        // Ensure this is a swarm manager
        if (server.Type != ServerType.SwarmManager)
        {
            return BadRequest(new { error = "Server is not a swarm manager. Only managers can remove nodes." });
        }

        try
        {
            _logger.LogInformation("Attempting to remove node {NodeId} from swarm on server {ServerId} (force={Force})",
                nodeId, serverId, force);

            var success = await _dockerService.RemoveNodeAsync(server, nodeId, force);

            if (success)
            {
                _logger.LogInformation("Successfully removed node {NodeId} from swarm", nodeId);
                return Ok(new { message = "Node removed successfully", nodeId });
            }
            else
            {
                _logger.LogWarning("RemoveNodeAsync returned false for node {NodeId}", nodeId);
                return BadRequest(new { error = "Failed to remove node" });
            }
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Cannot remove node {NodeId}: {Message}", nodeId, ex.Message);
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing node {NodeId} for server {ServerId}", nodeId, serverId);
            return StatusCode(500, new { error = "Failed to remove node", message = ex.Message });
        }
    }
}
