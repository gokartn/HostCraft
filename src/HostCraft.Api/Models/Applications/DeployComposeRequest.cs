using System.Collections.Generic;

namespace HostCraft.Api.Models.Applications;

/// <summary>
/// Request to deploy a Docker Compose application
/// </summary>
public record DeployComposeRequest(
    string Name,
    int ProjectId,
    int ServerId,
    string ComposeFile,
    string? Description = null,
    List<ComposeEnvironmentVariableRequest>? EnvironmentVariables = null
);
