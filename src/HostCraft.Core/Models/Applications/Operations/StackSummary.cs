using System;

namespace HostCraft.Core.Models.Applications.Operations;

/// <summary>
/// Summary information for a Docker stack.
/// </summary>
public record StackSummary(
    int ServerId,
    string ServerName,
    string StackName,
    int ServiceCount,
    DateTime? CreatedAt);
