using BCrypt.Net;
using Docker.DotNet;
using Docker.DotNet.Models;
using HostCraft.Core.Entities;
using HostCraft.Core.Interfaces;
using HostCraft.Core.Interfaces.Repositories;
using HostCraft.Core.Models.Results;
using HostCraft.Core.Models.SystemSettings;
using Microsoft.Extensions.Logging;

namespace HostCraft.Infrastructure.Services;

public class SystemSettingsService : ISystemSettingsService
{
    private readonly ISystemSettingsRepository _systemSettingsRepository;
    private readonly IProxyService _proxyService;
    private readonly ILogger<SystemSettingsService> _logger;

    public SystemSettingsService(
        ISystemSettingsRepository systemSettingsRepository,
        IProxyService proxyService,
        ILogger<SystemSettingsService> logger)
    {
        _systemSettingsRepository = systemSettingsRepository;
        _proxyService = proxyService;
        _logger = logger;
    }

    public Task<SystemSettings?> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        return _systemSettingsRepository.GetAsync(cancellationToken);
    }

    public async Task<OperationResult<SystemSettings>> ConfigureHostCraftAsync(
        ConfigureHostCraftCommand command,
        CancellationToken cancellationToken = default)
    {
        var settings = await _systemSettingsRepository.GetOrCreateAsync(cancellationToken);

        settings.HostCraftDomain = command.Domain;
        settings.HostCraftApiDomain = command.ApiDomain;
        settings.HostCraftEnableHttps = command.EnableHttps;
        settings.HostCraftLetsEncryptEmail = command.LetsEncryptEmail;
        settings.ConfiguredAt = DateTime.UtcNow;
        settings.UpdatedAt = DateTime.UtcNow;

        await _systemSettingsRepository.UpdateAsync(settings, cancellationToken);

        try
        {
            var configured = await _proxyService.ConfigureHostCraftDomainAsync(
                command.Domain,
                command.EnableHttps,
                command.LetsEncryptEmail,
                cancellationToken);

            if (!configured)
            {
                _logger.LogError("Proxy configuration failed for domain {Domain}", command.Domain);
                return OperationResult<SystemSettings>.Failure("Settings saved but proxy configuration failed. Check proxy service logs.");
            }

            settings.ProxyUpdatedAt = DateTime.UtcNow;
            settings.CertificateStatus = command.EnableHttps ? "Requesting..." : "Disabled";
            await _systemSettingsRepository.UpdateAsync(settings, cancellationToken);

            return OperationResult<SystemSettings>.SuccessResult(settings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to configure HostCraft domain {Domain}", command.Domain);
            return OperationResult<SystemSettings>.Failure($"Settings saved but proxy configuration failed: {ex.Message}");
        }
    }

    public async Task<OperationResult<SystemSettings>> ConfigureTraefikDashboardAsync(
        ConfigureTraefikDashboardCommand command,
        CancellationToken cancellationToken = default)
    {
        var settings = await _systemSettingsRepository.GetOrCreateAsync(cancellationToken);

        string? passwordHash = null;
        if (command.EnableAuth && !string.IsNullOrWhiteSpace(command.Password))
        {
            passwordHash = BCrypt.Net.BCrypt.HashPassword(command.Password, workFactor: 10)
                .Replace("$", "$$");
        }

        settings.TraefikDashboardDomain = command.DashboardDomain;
        settings.TraefikDashboardAuthEnabled = command.EnableAuth;
        settings.TraefikDashboardUsername = command.EnableAuth ? command.Username : null;
        settings.TraefikDashboardPasswordHash = passwordHash;
        settings.UpdatedAt = DateTime.UtcNow;

        await _systemSettingsRepository.UpdateAsync(settings, cancellationToken);

        try
        {
            var success = await _proxyService.ConfigureTraefikDashboardAsync(
                command.DashboardDomain,
                command.EnableAuth,
                command.Username,
                passwordHash,
                cancellationToken);

            if (!success)
            {
                _logger.LogWarning("Traefik dashboard configuration failed for domain {Domain}", command.DashboardDomain);
                return OperationResult<SystemSettings>.Failure("Settings saved but Traefik configuration failed. Traefik container may not be running.");
            }

            return OperationResult<SystemSettings>.SuccessResult(settings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error configuring Traefik dashboard for domain {Domain}", command.DashboardDomain);
            return OperationResult<SystemSettings>.Failure($"Settings saved but Traefik configuration failed: {ex.Message}");
        }
    }

    public async Task<OperationResult<ContainerLogsResult>> GetContainerLogsAsync(
        int lines,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var dockerClient = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock"))
                .CreateClient();

            string? webLogs = null;
            string? apiLogs = null;
            string? postgresLogs = null;

            var tasks = new List<Task>
            {
                CollectLogsAsync(dockerClient, "hostcraft_web", lines, value => webLogs = value, cancellationToken),
                CollectLogsAsync(dockerClient, "hostcraft_api", lines, value => apiLogs = value, cancellationToken),
                CollectLogsAsync(dockerClient, "hostcraft_postgres", lines, value => postgresLogs = value, cancellationToken)
            };

            await Task.WhenAll(tasks);

            return OperationResult<ContainerLogsResult>.SuccessResult(
                new ContainerLogsResult(webLogs, apiLogs, postgresLogs));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to collect container logs");
            return OperationResult<ContainerLogsResult>.Failure($"Failed to get container logs: {ex.Message}");
        }
    }

    private static async Task CollectLogsAsync(
        IDockerClient client,
        string nameFilter,
        int lines,
        Action<string> assign,
        CancellationToken cancellationToken)
    {
        var containers = await client.Containers.ListContainersAsync(
            new ContainersListParameters
            {
                All = true,
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["name"] = new Dictionary<string, bool> { [nameFilter] = true }
                }
            },
            cancellationToken);

        var container = containers.FirstOrDefault();
        if (container == null)
        {
            assign($"Container matching '{nameFilter}' not found");
            return;
        }

        var logsParameters = new ContainerLogsParameters
        {
            ShowStdout = true,
            ShowStderr = true,
            Tail = lines.ToString(),
            Timestamps = false
        };

        using var multiplexedStream = await client.Containers.GetContainerLogsAsync(
            container.ID,
            false,
            logsParameters,
            cancellationToken);

        using var memoryStream = new MemoryStream();
        await multiplexedStream.CopyOutputToAsync(Stream.Null, memoryStream, memoryStream, cancellationToken);
        memoryStream.Position = 0;

        using var reader = new StreamReader(memoryStream);
        assign(await reader.ReadToEndAsync(cancellationToken));
    }
}
