namespace HostCraft.Api.Models.Applications;

public record ComposeEnvironmentVariableRequest(
    string Key,
    string Value,
    bool IsSecret = false,
    string? Description = null
);
