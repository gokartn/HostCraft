using HostCraft.Core.Entities;
using HostCraft.Core.Models;

namespace HostCraft.Core.Interfaces;

/// <summary>
/// Service for managing database templates and deploying databases
/// </summary>
public interface IDatabaseTemplateService
{
    /// <summary>
    /// Get all available database templates
    /// </summary>
    Task<List<DatabaseTemplate>> GetAllTemplatesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a specific database template by ID
    /// </summary>
    Task<DatabaseTemplate?> GetTemplateByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deploy a database from a template
    /// </summary>
    /// <param name="templateId">Database template ID</param>
    /// <param name="name">Name for the database instance</param>
    /// <param name="serverId">Server to deploy to</param>
    /// <param name="projectId">Project to associate with</param>
        /// <param name="customDockerImage">Optional docker image override</param>
        /// <param name="customEnvVars">Optional custom environment variables (overrides defaults)</param>
    /// <param name="memoryLimitBytes">Optional memory limit</param>
    /// <param name="cpuLimit">Optional CPU limit</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Deployed application</returns>
    Task<DatabaseDeploymentResult> DeployDatabaseAsync(
        int templateId,
        string name,
        int serverId,
        int projectId,
            string? customDockerImage = null,
        Dictionary<string, string>? customEnvVars = null,
        long? memoryLimitBytes = null,
        double? cpuLimit = null,
        int? publishedPort = null,
        CancellationToken cancellationToken = default);
}
