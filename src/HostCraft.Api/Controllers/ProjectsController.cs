using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HostCraft.Core.Entities;
using HostCraft.Infrastructure.Persistence;

namespace HostCraft.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProjectsController : ControllerBase
{
    private readonly HostCraftDbContext _context;
    private readonly ILogger<ProjectsController> _logger;
    
    public ProjectsController(HostCraftDbContext context, ILogger<ProjectsController> logger)
    {
        _context = context;
        _logger = logger;
    }
    
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ProjectResponseDto>>> GetProjects()
    {
        var projects = await _context.Projects
            .Include(p => p.Applications)
            .ToListAsync();
        
        return Ok(projects.Select(p => new ProjectResponseDto
        {
            Id = p.Id,
            Name = p.Name,
            Description = p.Description,
            ApplicationCount = p.Applications.Count,
            CreatedAt = p.CreatedAt
        }));
    }
    
    [HttpGet("{id}")]
    public async Task<ActionResult<ProjectDetailDto>> GetProject(int id)
    {
        var project = await _context.Projects
            .Include(p => p.Applications)
            .ThenInclude(a => a.Server)
            .FirstOrDefaultAsync(p => p.Id == id);
        
        if (project == null)
            return NotFound();
        
        return new ProjectDetailDto
        {
            Id = project.Id,
            Name = project.Name,
            Description = project.Description,
            CreatedAt = project.CreatedAt,
            Applications = project.Applications.Select(a => new ProjectApplicationDto
            {
                Id = a.Id,
                Name = a.Name,
                DockerImage = a.DockerImage,
                ServerName = a.Server.Name,
                LastDeployedAt = a.LastDeployedAt
            }).ToList()
        };
    }
    
    [HttpPost]
    public async Task<ActionResult<ProjectResponseDto>> CreateProject(CreateProjectRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Project name is required" });
        
        var existingProject = await _context.Projects
            .FirstOrDefaultAsync(p => p.Name.ToLower() == request.Name.ToLower());
        
        if (existingProject != null)
            return BadRequest(new { error = "A project with this name already exists" });
        
        var project = new Project
        {
            Uuid = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            CreatedAt = DateTime.UtcNow
        };
        
        _context.Projects.Add(project);
        await _context.SaveChangesAsync();
        
        return CreatedAtAction(nameof(GetProject), new { id = project.Id }, new ProjectResponseDto
        {
            Id = project.Id,
            Name = project.Name,
            Description = project.Description,
            ApplicationCount = 0,
            CreatedAt = project.CreatedAt
        });
    }
    
    [HttpPut("{id}")]
    public async Task<ActionResult<ProjectResponseDto>> UpdateProject(int id, UpdateProjectRequest request)
    {
        var project = await _context.Projects
            .Include(p => p.Applications)
            .FirstOrDefaultAsync(p => p.Id == id);
        
        if (project == null)
            return NotFound();
        
        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            var existingProject = await _context.Projects
                .FirstOrDefaultAsync(p => p.Name.ToLower() == request.Name.ToLower() && p.Id != id);
            
            if (existingProject != null)
                return BadRequest(new { error = "A project with this name already exists" });
            
            project.Name = request.Name;
        }
        
        project.Description = request.Description;
        
        await _context.SaveChangesAsync();
        
        return new ProjectResponseDto
        {
            Id = project.Id,
            Name = project.Name,
            Description = project.Description,
            ApplicationCount = project.Applications.Count,
            CreatedAt = project.CreatedAt
        };
    }
    
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteProject(int id)
    {
        var project = await _context.Projects
            .Include(p => p.Applications)
            .FirstOrDefaultAsync(p => p.Id == id);
        
        if (project == null)
            return NotFound();
        
        if (project.Applications.Any())
            return BadRequest(new { error = "Cannot delete project with applications. Delete all applications first." });
        
        _context.Projects.Remove(project);
        await _context.SaveChangesAsync();
        
        return Ok(new { message = "Project deleted successfully" });
    }
}

public record CreateProjectRequest(string Name, string? Description);
public record UpdateProjectRequest(string Name, string? Description);

public record ProjectResponseDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int ApplicationCount { get; init; }
    public DateTime CreatedAt { get; init; }
}

public record ProjectDetailDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public DateTime CreatedAt { get; init; }
    public List<ProjectApplicationDto> Applications { get; init; } = new();
}

public record ProjectApplicationDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? DockerImage { get; init; }
    public string ServerName { get; init; } = string.Empty;
    public DateTime? LastDeployedAt { get; init; }
}
