namespace HostCraft.Core.Interfaces;

/// <summary>
/// Service for managing server CRUD operations with validation.
/// </summary>
public interface IServerManagementService
{
    /// <summary>
    /// Create a new server with validation and SSH key setup.
    /// </summary>
    Task<ServerCreationResult> CreateServerAsync(ServerCreationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update an existing server with validation.
    /// </summary>
    Task<ServerUpdateResult> UpdateServerAsync(int serverId, ServerUpdateRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a server and cleanup resources.
    /// </summary>
    Task<ServerDeletionResult> DeleteServerAsync(int serverId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate server connection and Docker availability.
    /// </summary>
    Task<ServerConnectionValidation> ValidateServerAsync(int serverId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate connectivity for a server that has not been created yet.
    /// </summary>
    Task<ServerValidationOutcome> ValidateNewServerAsync(ServerCreationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate an existing server (including optional swarm rejoin) and update status.
    /// </summary>
    Task<ServerValidationOutcome> ValidateExistingServerAsync(int serverId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Configure or refresh the localhost server entry.
    /// </summary>
    Task<LocalhostConfigurationResult> ConfigureLocalhostAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Request to create a new server.
/// </summary>
public record ServerCreationRequest(
    string Name,
    string Host,
    int Port,
    string User,
    string? PrivateKeyContent,
    Core.Enums.ServerType Type,
    Core.Enums.ProxyType ProxyType,
    string? Region,
    string? DefaultLetsEncryptEmail = null);

/// <summary>
/// Request to update an existing server.
/// </summary>
public record ServerUpdateRequest(
    string? Name,
    string? Host,
    int? Port,
    string? User,
    string? PrivateKeyContent,
    Core.Enums.ServerType? Type,
    Core.Enums.ProxyType? ProxyType,
    string? DefaultLetsEncryptEmail = null);

/// <summary>
/// Result of server creation.
/// </summary>
public record ServerCreationResult(
    bool Success,
    string Message,
    int? ServerId = null,
    string? ErrorDetails = null);

/// <summary>
/// Result of server update.
/// </summary>
public record ServerUpdateResult(
    bool Success,
    string Message,
    string? ErrorDetails = null);

/// <summary>
/// Result of server deletion.
/// </summary>
public record ServerDeletionResult(
    bool Success,
    string Message,
    string? ErrorDetails = null);

/// <summary>
/// Result of server validation.
/// </summary>
public record ServerConnectionValidation(
    bool Success,
    string Message,
    Core.Enums.ServerStatus? Status = null,
    string? DockerVersion = null,
    bool? IsSwarm = null,
    string? ErrorDetails = null);

/// <summary>
/// Detailed validation outcome used for both new and existing servers.
/// </summary>
public record ServerValidationOutcome(
    bool Success,
    string Message,
    SystemInfo? SystemInfo = null,
    bool NotFound = false,
    bool? Rejoined = null,
    string? RejoinError = null,
    Core.Enums.ServerStatus? UpdatedStatus = null,
    Core.Enums.ServerType? PreviousType = null,
    Core.Enums.ServerType? UpdatedType = null,
    string? ErrorDetails = null);

/// <summary>
/// Result of configuring or refreshing the localhost server entry.
/// </summary>
public record LocalhostConfigurationResult(
    bool Success,
    string Message,
    Core.Entities.Server? Server = null,
    bool DockerAvailable = true,
    string? ErrorDetails = null);
