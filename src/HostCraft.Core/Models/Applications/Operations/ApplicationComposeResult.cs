using System;

namespace HostCraft.Core.Models.Applications.Operations;

/// <summary>
/// Result of creating a Docker Compose application record.
/// </summary>
public record ApplicationComposeResult(
    int ApplicationId,
    string Name,
    string? Description,
    int ServerId,
    string ServerName,
    int ProjectId,
    string ProjectName,
    DateTime CreatedAt);
