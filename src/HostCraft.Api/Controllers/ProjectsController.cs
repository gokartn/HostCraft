using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HostCraft.Api.Models.Projects;
using HostCraft.Core.Constants;
using HostCraft.Core.Entities;
using HostCraft.Core.Interfaces.Repositories;

namespace HostCraft.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProjectsController : ControllerBase
{
    private readonly IProjectRepository _projectRepository;
    private readonly ILogger<ProjectsController> _logger;
    private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;
    private const string GlobalRoute = "system/global";
    
    public ProjectsController(IProjectRepository projectRepository, ILogger<ProjectsController> logger)
    {
        _projectRepository = projectRepository;
        _logger = logger;
    }
    
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ProjectResponseDto>>> GetProjects()
    {
        await EnsureGlobalProjectAsync();

        var projects = await _projectRepository.GetAllWithApplicationsAsync();
        
        return Ok(projects.Select(p => new ProjectResponseDto
        {
            Id = p.Id,
            Name = p.Name,
            Description = p.Description,
            ApplicationCount = p.Applications.Count,
            CreatedAt = p.CreatedAt
        }));
    }

    [HttpGet(GlobalRoute)]
    public async Task<ActionResult<ProjectResponseDto>> GetGlobalProject()
    {
        var project = await EnsureGlobalProjectAsync();

        return Ok(new ProjectResponseDto
        {
            Id = project.Id,
            Name = project.Name,
            Description = project.Description,
            ApplicationCount = project.Applications.Count,
            CreatedAt = project.CreatedAt
        });
    }
    
    [HttpGet("{id}")]
    public async Task<ActionResult<ProjectDetailDto>> GetProject(int id)
    {
        var project = await _projectRepository.GetByIdWithApplicationsAsync(id);
        
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
        
        var nameExists = await _projectRepository.ExistsByNameAsync(request.Name);
        if (nameExists)
            return BadRequest(new { error = "A project with this name already exists" });
        
        var project = new Project
        {
            Uuid = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            CreatedAt = DateTime.UtcNow
        };
        
        await _projectRepository.AddAsync(project);
        
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
        var project = await _projectRepository.GetByIdWithApplicationsAsync(id);
        
        if (project == null)
            return NotFound();

        if (IsGlobalProject(project))
            return BadRequest(new { error = "Global Deployments workspace cannot be edited" });
        
        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            var existingProject = await _projectRepository.ExistsByNameAsync(request.Name, id);
            if (existingProject)
                return BadRequest(new { error = "A project with this name already exists" });
            
            project.Name = request.Name;
        }
        
        project.Description = request.Description;
        await _projectRepository.UpdateAsync(project);
        
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
        var project = await _projectRepository.GetByIdWithApplicationsAsync(id);
        
        if (project == null)
            return NotFound();

        if (IsGlobalProject(project))
            return BadRequest(new { error = "Cannot delete the Global Deployments workspace" });
        
        if (project.Applications.Any())
            return BadRequest(new { error = "Cannot delete project with applications. Delete all applications first." });
        
        await _projectRepository.DeleteAsync(project);
        
        return Ok(new { message = "Project deleted successfully" });
    }

    private async Task<Project> EnsureGlobalProjectAsync()
    {
        return await _projectRepository.GetOrCreateGlobalAsync(SystemProjects.GlobalDeploymentsDescription);
    }

    private static bool IsGlobalProject(Project project) =>
        NameComparer.Equals(project.Name, SystemProjects.GlobalDeployments);
}
