namespace HostCraft.Core.Enums;

/// <summary>
/// Defines the source type for application deployment.
/// </summary>
public enum ApplicationSourceType
{
    /// <summary>
    /// Deploy from a pre-built Docker image.
    /// </summary>
    DockerImage = 0,
    
    /// <summary>
    /// Deploy using a docker-compose.yml file.
    /// </summary>
    DockerCompose = 1,
    
    /// <summary>
    /// Build from Dockerfile in Git repository and deploy.
    /// </summary>
    Dockerfile = 2,
    
    /// <summary>
    /// Deploy from Git repository (buildpacks or detected Dockerfile).
    /// </summary>
    Git = 3
}
