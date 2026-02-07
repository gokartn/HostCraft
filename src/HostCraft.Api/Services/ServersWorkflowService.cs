using HostCraft.Api.Models.Servers;
using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using HostCraft.Core.Interfaces;
using HostCraft.Core.Interfaces.Repositories;
using Microsoft.AspNetCore.Http;

namespace HostCraft.Api.Services;

public class ServersWorkflowService : IServersWorkflowService
{
    private readonly IServerRepository _serverRepository;
    private readonly IPrivateKeyRepository _privateKeyRepository;
    private readonly IRegionRepository _regionRepository;
    private readonly IDockerService _dockerService;
    private readonly ISwarmManagementService _swarmManagementService;
    private readonly IServerManagementService _serverManagementService;
    private readonly IServerOrchestrationService _serverOrchestrationService;
    private readonly IServerConfigurationService _serverConfigurationService;

    public ServersWorkflowService(
        IServerRepository serverRepository,
        IPrivateKeyRepository privateKeyRepository,
        IRegionRepository regionRepository,
        IDockerService dockerService,
        ISwarmManagementService swarmManagementService,
        IServerManagementService serverManagementService,
        IServerOrchestrationService serverOrchestrationService,
        IServerConfigurationService serverConfigurationService)
    {
        _serverRepository = serverRepository;
        _privateKeyRepository = privateKeyRepository;
        _regionRepository = regionRepository;
        _dockerService = dockerService;
        _swarmManagementService = swarmManagementService;
        _serverManagementService = serverManagementService;
        _serverOrchestrationService = serverOrchestrationService;
        _serverConfigurationService = serverConfigurationService;
    }

    public async Task<ApiActionResult<IEnumerable<ServerListDto>>> GetServersAsync(bool paged, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (!paged)
        {
            var servers = await _serverRepository.GetAllWithRegionAsync();
            return ApiActionResult<IEnumerable<ServerListDto>>.Ok(MapServerDtos(servers));
        }

        var (items, totalCount) = await _serverRepository.GetPagedWithRegionAsync(page, pageSize);
        var mapped = MapServerDtos(items);

        return ApiActionResult<IEnumerable<ServerListDto>>.Ok(mapped, StatusCodes.Status200OK);
    }

    public async Task<ApiActionResult<Server>> GetServerAsync(int id, CancellationToken cancellationToken)
    {
        var server = await _serverRepository.GetByIdWithPrivateKeyAndRegionAsync(id);
        return server == null
            ? ApiActionResult<Server>.Fail(StatusCodes.Status404NotFound, "Server not found")
            : ApiActionResult<Server>.Ok(server);
    }

    public async Task<ApiActionResult<Server>> CreateServerAsync(CreateServerRequest request, CancellationToken cancellationToken)
    {
        var creationRequest = new ServerCreationRequest(
            request.Name,
            request.Host,
            request.Port,
            request.User,
            request.PrivateKeyContent,
            request.Type,
            request.ProxyType,
            request.Region,
            request.DefaultLetsEncryptEmail);

        var result = await _serverManagementService.CreateServerAsync(creationRequest, cancellationToken);

        if (!result.Success)
            return ApiActionResult<Server>.Fail(StatusCodes.Status400BadRequest, result.Message ?? "Failed to create server");

        var server = await _serverRepository.GetByIdWithPrivateKeyAndRegionAsync(result.ServerId!.Value);
        if (server == null)
            return ApiActionResult<Server>.Fail(StatusCodes.Status500InternalServerError, "Server created but not found");

        _ = Task.Run(async () => await _serverOrchestrationService.ValidateAndConfigureServerAsync(server.Id), cancellationToken);

        return ApiActionResult<Server>.Ok(server, StatusCodes.Status201Created);
    }

    public async Task<ApiActionResult> UpdateServerAsync(int id, UpdateServerRequest request, CancellationToken cancellationToken)
    {
        var updateRequest = new ServerUpdateRequest(
            request.Name,
            request.Host,
            request.Port,
            request.User,
            request.PrivateKeyContent,
            request.Type,
            request.ProxyType,
            request.DefaultLetsEncryptEmail);

        var result = await _serverManagementService.UpdateServerAsync(id, updateRequest, cancellationToken);

        if (!result.Success)
        {
            if (string.Equals(result.Message, "Server not found", StringComparison.OrdinalIgnoreCase))
                return ApiActionResult.Fail(StatusCodes.Status404NotFound, result.Message ?? "Server not found");

            return ApiActionResult.Fail(StatusCodes.Status400BadRequest, result.Message ?? "Failed to update server");
        }

        _ = Task.Run(async () => await _serverOrchestrationService.RevalidateServerAsync(id), cancellationToken);

        return ApiActionResult.Ok(StatusCodes.Status204NoContent);
    }

    public async Task<ApiActionResult> DeleteServerAsync(int id, CancellationToken cancellationToken)
    {
        var result = await _serverManagementService.DeleteServerAsync(id, cancellationToken);

        if (!result.Success)
        {
            if (string.Equals(result.Message, "Server not found", StringComparison.OrdinalIgnoreCase))
                return ApiActionResult.Fail(StatusCodes.Status404NotFound, result.Message ?? "Server not found");

            return ApiActionResult.Fail(StatusCodes.Status409Conflict, result.Message ?? "Failed to delete server");
        }

        return ApiActionResult.Ok(StatusCodes.Status204NoContent);
    }

    public async Task<ApiActionResult<Server>> ConfigureLocalhostAsync(CancellationToken cancellationToken)
    {
        var result = await _serverManagementService.ConfigureLocalhostAsync(cancellationToken);

        if (!result.Success)
        {
            var status = result.DockerAvailable ? StatusCodes.Status500InternalServerError : StatusCodes.Status400BadRequest;
            return ApiActionResult<Server>.Fail(status, result.Message ?? "Failed to configure localhost");
        }

        return ApiActionResult<Server>.Ok(result.Server!);
    }

    public async Task<ApiActionResult<ServerValidationResult>> ValidateNewServerAsync(CreateServerRequest request, CancellationToken cancellationToken)
    {
        var creationRequest = new ServerCreationRequest(
            request.Name,
            request.Host,
            request.Port,
            request.User,
            request.PrivateKeyContent,
            request.Type,
            request.ProxyType,
            request.Region,
            request.DefaultLetsEncryptEmail);

        var result = await _serverManagementService.ValidateNewServerAsync(creationRequest, cancellationToken);

        if (!result.Success)
        {
            return ApiActionResult<ServerValidationResult>.Fail(StatusCodes.Status400BadRequest, result.Message ?? "Validation failed");
        }

        return ApiActionResult<ServerValidationResult>.Ok(new ServerValidationResult
        {
            IsValid = true,
            SystemInfo = result.SystemInfo,
            Message = result.Message
        });
    }

    public async Task<ApiActionResult<ServerValidationResult>> ValidateExistingServerAsync(int id, CancellationToken cancellationToken)
    {
        var result = await _serverManagementService.ValidateExistingServerAsync(id, cancellationToken);

        if (result.NotFound)
        {
            return ApiActionResult<ServerValidationResult>.Fail(StatusCodes.Status404NotFound, result.Message ?? "Server not found");
        }

        if (!result.Success)
        {
            return ApiActionResult<ServerValidationResult>.Fail(StatusCodes.Status400BadRequest, result.Message ?? "Validation failed");
        }

        return ApiActionResult<ServerValidationResult>.Ok(new ServerValidationResult
        {
            IsValid = true,
            SystemInfo = result.SystemInfo,
            Message = result.Rejoined == true
                ? "Server rejoined swarm successfully"
                : result.Message
        });
    }

    public async Task<ApiActionResult<IEnumerable<ContainerInfo>>> GetContainersAsync(int id, CancellationToken cancellationToken)
    {
        var server = await _serverRepository.GetByIdWithPrivateKeyAsync(id);

        if (server == null)
            return ApiActionResult<IEnumerable<ContainerInfo>>.Fail(StatusCodes.Status404NotFound, "Server not found");

        try
        {
            var containers = await _dockerService.ListContainersAsync(server);
            return ApiActionResult<IEnumerable<ContainerInfo>>.Ok(containers);
        }
        catch (Exception ex)
        {
            return ApiActionResult<IEnumerable<ContainerInfo>>.Fail(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }

    public async Task<ApiActionResult<IEnumerable<ServiceInfo>>> GetServicesAsync(int id, CancellationToken cancellationToken)
    {
        var server = await _serverRepository.GetByIdWithPrivateKeyAsync(id);

        if (server == null)
            return ApiActionResult<IEnumerable<ServiceInfo>>.Fail(StatusCodes.Status404NotFound, "Server not found");

        if (!server.IsSwarm)
            return ApiActionResult<IEnumerable<ServiceInfo>>.Fail(StatusCodes.Status400BadRequest, "Server is not in Swarm mode");

        try
        {
            var services = await _dockerService.ListServicesAsync(server);
            return ApiActionResult<IEnumerable<ServiceInfo>>.Ok(services);
        }
        catch (Exception ex)
        {
            return ApiActionResult<IEnumerable<ServiceInfo>>.Fail(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }

    public async Task<ApiActionResult> RefreshSwarmStatusAsync(int id, CancellationToken cancellationToken)
    {
        var result = await _swarmManagementService.RefreshSwarmStatusWithRecoveryAsync(id, cancellationToken);

        if (result.NotFound)
            return ApiActionResult.Fail(StatusCodes.Status404NotFound, result.Message ?? "Server not found");

        if (!result.Success)
            return ApiActionResult.Fail(StatusCodes.Status500InternalServerError, result.Message ?? "Failed to refresh swarm status");

        return ApiActionResult.Ok(StatusCodes.Status200OK);
    }

    public async Task<ApiActionResult> InitializeSwarmAsync(int id, CancellationToken cancellationToken)
    {
        try
        {
            await _swarmManagementService.InitializeSwarmAsync(id);
            var server = await _serverRepository.GetByIdAsync(id);
            return ApiActionResult.Ok(StatusCodes.Status200OK);
        }
        catch (InvalidOperationException ex)
        {
            var status = ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status500InternalServerError;
            return ApiActionResult.Fail(status, ex.Message);
        }
    }

    public async Task<ApiActionResult> JoinAsManagerAsync(int existingManagerId, JoinManagerRequest request, CancellationToken cancellationToken)
    {
        var result = await _swarmManagementService.JoinAsManagerAsync(existingManagerId, request.ServerIdToJoin);

        if (!result.Success)
        {
            if (result.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
                return ApiActionResult.Fail(StatusCodes.Status404NotFound, result.Message ?? "Server not found");
            if (result.Message.Contains("not online", StringComparison.OrdinalIgnoreCase) ||
                result.Message.Contains("must be standalone", StringComparison.OrdinalIgnoreCase) ||
                result.Message.Contains("not a swarm manager", StringComparison.OrdinalIgnoreCase))
                return ApiActionResult.Fail(StatusCodes.Status400BadRequest, result.Message ?? "Validation failed");
            return ApiActionResult.Fail(StatusCodes.Status500InternalServerError, result.Message ?? "Join failed");
        }

        return ApiActionResult.Ok(StatusCodes.Status200OK);
    }

    public async Task<ApiActionResult> PromoteToManagerAsync(int id, CancellationToken cancellationToken)
    {
        var result = await _swarmManagementService.PromoteToManagerAsync(id);

        if (!result.Success)
        {
            if (result.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
                return ApiActionResult.Fail(StatusCodes.Status404NotFound, result.Message ?? "Server not found");
            if (result.Message.Contains("must be", StringComparison.OrdinalIgnoreCase) || result.Message.Contains("does not have", StringComparison.OrdinalIgnoreCase))
                return ApiActionResult.Fail(StatusCodes.Status400BadRequest, result.Message ?? "Validation failed");
            return ApiActionResult.Fail(StatusCodes.Status500InternalServerError, result.Message ?? "Promotion failed");
        }

        return ApiActionResult.Ok(StatusCodes.Status200OK);
    }

    public async Task<ApiActionResult> AutoConfigureServerAsync(int id, CancellationToken cancellationToken)
    {
        var result = await _serverConfigurationService.StartAutoConfigureAsync(id, cancellationToken);

        if (result.NotFound)
            return ApiActionResult.Fail(StatusCodes.Status404NotFound, result.Message ?? "Server not found");

        if (!result.Success)
            return ApiActionResult.Fail(StatusCodes.Status400BadRequest, result.Message ?? "Auto-configure failed");

        return ApiActionResult.Ok(StatusCodes.Status200OK);
    }

    public async Task<ApiActionResult<SystemInfo>> GetSystemInfoAsync(int id, CancellationToken cancellationToken)
    {
        var server = await _serverRepository.GetByIdWithPrivateKeyAsync(id);

        if (server == null)
            return ApiActionResult<SystemInfo>.Fail(StatusCodes.Status404NotFound, "Server not found");

        try
        {
            var systemInfo = await _dockerService.GetSystemInfoAsync(server);
            return ApiActionResult<SystemInfo>.Ok(systemInfo);
        }
        catch (Exception ex)
        {
            return ApiActionResult<SystemInfo>.Fail(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }

    public async Task<ApiActionResult> UpdateWizardStepAsync(int id, WizardStepUpdate request, CancellationToken cancellationToken)
    {
        var server = await _serverRepository.GetByIdAsync(id);
        if (server == null)
            return ApiActionResult.Fail(StatusCodes.Status404NotFound, $"Server {id} not found");

        server.WizardStep = request.WizardStep;
        server.IsWizardSetup = true;

        await _serverRepository.UpdateAsync(server);

        return ApiActionResult.Ok(StatusCodes.Status200OK);
    }

    public async Task<ApiActionResult> CompleteWizardAsync(int id, CancellationToken cancellationToken)
    {
        var server = await _serverRepository.GetByIdAsync(id);
        if (server == null)
            return ApiActionResult.Fail(StatusCodes.Status404NotFound, $"Server {id} not found");

        server.WizardStep = null;
        server.IsWizardSetup = true;
        server.WizardCompletedAt = DateTime.UtcNow;

        await _serverRepository.UpdateAsync(server);

        return ApiActionResult.Ok(StatusCodes.Status200OK);
    }

    public async Task<ApiActionResult<object>> GetPublicKeyAsync(int id, CancellationToken cancellationToken)
    {
        var server = await _serverRepository.GetByIdWithPrivateKeyAsync(id);

        if (server == null)
            return ApiActionResult<object>.Fail(StatusCodes.Status404NotFound, "Server not found");

        if (server.PrivateKey == null)
            return ApiActionResult<object>.Fail(StatusCodes.Status404NotFound, "No private key configured for this server");

        try
        {
            var keyFile = new Renci.SshNet.PrivateKeyFile(
                new MemoryStream(System.Text.Encoding.UTF8.GetBytes(server.PrivateKey.KeyData)),
                server.PrivateKey.Passphrase
            );

            using var publicKeyStream = new MemoryStream();
            using var writer = new BinaryWriter(publicKeyStream);

            var algorithm = System.Text.Encoding.ASCII.GetBytes("ssh-rsa");
            writer.Write(algorithm.Length);
            writer.Write(algorithm);

            if (keyFile.Key is Renci.SshNet.Security.RsaKey key)
            {
                var exponent = key.Exponent.ToByteArray().Reverse().SkipWhile(b => b == 0).Reverse().ToArray();
                writer.Write(exponent.Length);
                writer.Write(exponent);

                var modulus = key.Modulus.ToByteArray().Reverse().SkipWhile(b => b == 0).Reverse().ToArray();
                writer.Write(modulus.Length);
                writer.Write(modulus);
            }

            var publicKeyBytes = publicKeyStream.ToArray();
            var publicKey = $"ssh-rsa {Convert.ToBase64String(publicKeyBytes)} HostCraft-{server.Name}";

            return ApiActionResult<object>.Ok(new
            {
                publicKey,
                instruction = $"Copy this public key and add it to ~/.ssh/authorized_keys on {server.Host}",
                manualCommand = $"ssh {server.Username}@{server.Host} -p {server.Port}\nmkdir -p ~/.ssh\necho '{publicKey}' >> ~/.ssh/authorized_keys\nchmod 600 ~/.ssh/authorized_keys\nchmod 700 ~/.ssh"
            });
        }
        catch (Exception ex)
        {
            return ApiActionResult<object>.Fail(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }

    public async Task<ApiActionResult> GetJoinTokensAsync(int id, CancellationToken cancellationToken)
    {
        try
        {
            var (workerToken, managerToken) = await _swarmManagementService.GetJoinTokensAsync(id);

            return ApiActionResult.Ok(StatusCodes.Status200OK);
        }
        catch (InvalidOperationException ex)
        {
            if (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
                return ApiActionResult.Fail(StatusCodes.Status404NotFound, ex.Message);

            if (ex.Message.Contains("not a swarm manager", StringComparison.OrdinalIgnoreCase))
                return ApiActionResult.Fail(StatusCodes.Status400BadRequest, ex.Message);

            return ApiActionResult.Fail(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }

    private static List<ServerListDto> MapServerDtos(IEnumerable<Server> servers)
    {
        return servers.Select(static s => new ServerListDto
        {
            Id = s.Id,
            Name = s.Name,
            Host = s.Host,
            Port = s.Port,
            Type = s.Type,
            Status = s.Status,
            Region = s.Region?.Name,
            SwarmManagerCount = s.SwarmManagerCount,
            SwarmWorkerCount = s.SwarmWorkerCount,
            IsSwarmManager = s.IsSwarmManager,
            IsSwarmWorker = s.IsSwarmWorker,
            SwarmNodeId = s.SwarmNodeId,
            SwarmNodeState = s.SwarmNodeState,
            SwarmNodeAvailability = s.SwarmNodeAvailability,
            ProxyType = s.ProxyType,
            CreatedAt = s.CreatedAt,
            LastHealthCheck = s.LastHealthCheck
        }).ToList();
    }
}
