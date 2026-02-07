using HostCraft.Api.Models.DatabaseTemplates;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HostCraft.Core.Interfaces;
using HostCraft.Core.Entities;
using HostCraft.Core.Models;

namespace HostCraft.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DatabaseTemplatesController : ControllerBase
{
    private readonly IDatabaseTemplateService _databaseTemplateService;
    private readonly ILogger<DatabaseTemplatesController> _logger;

    public DatabaseTemplatesController(
        IDatabaseTemplateService databaseTemplateService,
        ILogger<DatabaseTemplatesController> logger)
    {
        _databaseTemplateService = databaseTemplateService;
        _logger = logger;
    }

    /// <summary>
    /// Get all available database templates
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<DatabaseTemplateDto>>> GetTemplates()
    {
        _logger.LogInformation("[DatabaseTemplatesController] GetTemplates called");
        try
        {
            var templates = await _databaseTemplateService.GetAllTemplatesAsync();
            _logger.LogInformation("[DatabaseTemplatesController] Retrieved {Count} templates", templates.Count);
            return Ok(templates.Select(t => new DatabaseTemplateDto
            {
                Id = t.Id,
                Name = t.Name,
                Type = t.Type.ToString(),
                DockerImage = t.DockerImage,
                DefaultPort = t.DefaultPort,
                Category = t.Category,
                Description = t.Description,
                IconUrl = t.IconUrl,
                RecommendedMemoryMB = t.RecommendedMemoryBytes.HasValue
                    ? t.RecommendedMemoryBytes.Value / 1024 / 1024
                    : null,
                RecommendedCpuCores = t.RecommendedCpuLimit,
                Version = t.Version,
                DisplayName = t.DisplayName
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching database templates");
            return StatusCode(500, new { error = "Failed to fetch database templates" });
        }
    }

    /// <summary>
    /// Get a specific database template by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<DatabaseTemplateDetailDto>> GetTemplate(int id)
    {
        _logger.LogInformation("[DatabaseTemplatesController] GetTemplate called with id={Id}", id);
        try
        {
            _logger.LogInformation("[DatabaseTemplatesController] Fetching template from database...");
            var template = await _databaseTemplateService.GetTemplateByIdAsync(id);
            if (template == null)
            {
                _logger.LogWarning("[DatabaseTemplatesController] Template {Id} not found", id);
                return NotFound(new { error = $"Database template {id} not found" });
            }

            _logger.LogInformation("[DatabaseTemplatesController] Template found: {Name}, Type={Type}", template.Name, template.Type);
            var definitions = DatabaseTemplateBestPractices.GetDefinitions(template.Type);
            _logger.LogInformation("[DatabaseTemplatesController] Got {Count} definitions for type {Type}", definitions.Count, template.Type);

            var result = new DatabaseTemplateDetailDto
            {
                Id = template.Id,
                Name = template.Name,
                Type = template.Type.ToString(),
                DockerImage = template.DockerImage,
                DefaultPort = template.DefaultPort,
                DefaultEnvironmentVariables = template.DefaultEnvironmentVariables,
                DefaultVolumePath = template.DefaultVolumePath,
                HealthCheckCommand = template.HealthCheckCommand,
                Category = template.Category,
                Description = template.Description,
                IconUrl = template.IconUrl,
                RecommendedMemoryMB = template.RecommendedMemoryBytes.HasValue
                    ? template.RecommendedMemoryBytes.Value / 1024 / 1024
                    : null,
                RecommendedCpuCores = template.RecommendedCpuLimit,
                CreatedAt = template.CreatedAt,
                EnvironmentVariables = definitions
                    .Select(def => new EnvironmentVariableDefinitionDto
                    {
                        Key = def.Key,
                        Label = def.Label,
                        Description = def.Description,
                        IsSecret = def.IsSecret,
                        IsRequired = def.IsRequired,
                        Strategy = def.Strategy.ToString(),
                        DefaultValue = def.DefaultValue,
                        Length = def.Length,
                        Prefix = def.Prefix,
                        Suffix = def.Suffix,
                        DisplayInWizard = def.DisplayInWizard,
                        SuggestedValue = DatabaseTemplateBestPractices.GetSuggestedValue(def, template.Name, template.Name)
                    })
                    .ToList()
            };

            _logger.LogInformation("[DatabaseTemplatesController] Returning template detail with {EnvCount} env vars", result.EnvironmentVariables.Count);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching database template {Id}", id);
            return StatusCode(500, new { error = "Failed to fetch database template" });
        }
    }

    /// <summary>
    /// Deploy a database from a template
    /// </summary>
    [HttpPost("{id}/deploy")]
    public async Task<ActionResult<DeployDatabaseResponseDto>> DeployDatabase(int id, [FromBody] DeployDatabaseRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { error = "Database name is required" });
        }

        if (request.ServerId <= 0)
        {
            return BadRequest(new { error = "Valid server ID is required" });
        }

        if (request.ProjectId <= 0)
        {
            return BadRequest(new { error = "Valid project ID is required" });
        }

        try
        {
            _logger.LogInformation("Deploying database from template {TemplateId} with name {Name}", id, request.Name);

            // Convert memory from MB to bytes if provided
            long? memoryBytes = request.MemoryLimitMB.HasValue
                ? request.MemoryLimitMB.Value * 1024L * 1024L
                : null;

            var deployment = await _databaseTemplateService.DeployDatabaseAsync(
                templateId: id,
                name: request.Name,
                serverId: request.ServerId,
                projectId: request.ProjectId,
                customDockerImage: request.DockerImage,
                customEnvVars: request.EnvironmentVariables,
                memoryLimitBytes: memoryBytes,
                cpuLimit: request.CpuLimitCores,
                cancellationToken: HttpContext.RequestAborted);

            var application = deployment.Application;

            _logger.LogInformation("Successfully deployed database {Name} with application ID {Id}", request.Name, application.Id);

            return Ok(new DeployDatabaseResponseDto
            {
                ApplicationId = application.Id,
                Name = application.Name,
                Message = $"Database {application.Name} deployed successfully",
                DeployedAt = application.CreatedAt,
                PublishedPort = application.PublishedPort,
                DockerImage = application.DockerImage,
                EnvironmentVariables = deployment.ResolvedEnvironmentVariables
                    .Select(env => new ResolvedEnvironmentVariableDto
                    {
                        Key = env.Key,
                        Label = env.Label,
                        Value = env.Value,
                        IsSecret = env.IsSecret,
                        IsUserProvided = env.IsUserProvided,
                        Description = env.Description,
                        DisplayInWizard = env.DisplayInWizard
                    })
                    .ToList()
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation while deploying database from template {TemplateId}", id);
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deploying database from template {TemplateId}", id);
            return StatusCode(500, new { error = "Failed to deploy database", details = ex.Message });
        }
    }
}

