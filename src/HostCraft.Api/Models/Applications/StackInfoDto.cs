using System;

namespace HostCraft.Api.Models.Applications;

public record StackInfoDto(
    string Name,
    int ServiceCount,
    DateTime CreatedAt);
