using HostCraft.Api.Models.SystemSettings;
using HostCraft.Core.Interfaces;
using HostCraft.Core.Models.SystemSettings;
using Microsoft.AspNetCore.Http;

namespace HostCraft.Api.Services;

public class SystemSettingsWorkflowService : ISystemSettingsWorkflowService
{
    private readonly ISystemSettingsService _systemSettingsService;

    public SystemSettingsWorkflowService(ISystemSettingsService systemSettingsService)
    {
        _systemSettingsService = systemSettingsService;
    }

    public async Task<ApiActionResult<SystemSettingsDto>> GetSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await _systemSettingsService.GetSettingsAsync(cancellationToken);

        if (settings == null)
        {
            return ApiActionResult<SystemSettingsDto>.Ok(new SystemSettingsDto
            {
                HostCraftDomain = null,
                HostCraftApiDomain = null,
                HostCraftEnableHttps = true,
                HostCraftLetsEncryptEmail = null,
                CertificateStatus = null,
                TraefikDashboardDomain = null,
                TraefikDashboardAuthEnabled = false,
                TraefikDashboardUsername = null
            });
        }

        return ApiActionResult<SystemSettingsDto>.Ok(MapSettings(settings));
    }

    public async Task<ApiActionResult<ConfigureHostCraftResponse>> ConfigureHostCraftAsync(ConfigureHostCraftRequest request, CancellationToken cancellationToken)
    {
        var command = new ConfigureHostCraftCommand(
            request.Domain,
            request.ApiDomain,
            request.EnableHttps,
            request.LetsEncryptEmail);

        var result = await _systemSettingsService.ConfigureHostCraftAsync(command, cancellationToken);

        if (!result.Success)
        {
            return ApiActionResult<ConfigureHostCraftResponse>.Fail(
                StatusCodes.Status500InternalServerError,
                result.ErrorMessage ?? "Configuration failed");
        }

        return ApiActionResult<ConfigureHostCraftResponse>.Ok(new ConfigureHostCraftResponse
        {
            Success = true,
            Message = $"HostCraft domain configured successfully! Your panel is now accessible at {(request.EnableHttps ? "https" : "http")}://{request.Domain}. SSL certificate will be automatically provisioned.",
            Domain = request.Domain,
            HttpsEnabled = request.EnableHttps
        });
    }

    public async Task<ApiActionResult<ConfigureTraefikDashboardResponse>> ConfigureTraefikDashboardAsync(
        ConfigureTraefikDashboardRequest request,
        CancellationToken cancellationToken)
    {
        var command = new ConfigureTraefikDashboardCommand(
            request.DashboardDomain,
            request.EnableAuth,
            request.Username,
            request.Password);

        var result = await _systemSettingsService.ConfigureTraefikDashboardAsync(command, cancellationToken);

        if (!result.Success)
        {
            return ApiActionResult<ConfigureTraefikDashboardResponse>.Fail(
                StatusCodes.Status500InternalServerError,
                result.ErrorMessage ?? "Configuration failed");
        }

        var message = string.IsNullOrEmpty(request.DashboardDomain)
            ? "Traefik dashboard configuration removed. Dashboard is now only accessible via port 8080."
            : $"Traefik dashboard configured successfully! Access it at https://{request.DashboardDomain}" +
              (request.EnableAuth ? $" (Username: {request.Username})" : " (No authentication)");

        return ApiActionResult<ConfigureTraefikDashboardResponse>.Ok(new ConfigureTraefikDashboardResponse
        {
            Success = true,
            Message = message,
            DashboardDomain = request.DashboardDomain,
            AuthEnabled = request.EnableAuth
        });
    }

    public async Task<ApiActionResult<ContainerLogsResponse>> GetContainerLogsAsync(int lines, CancellationToken cancellationToken)
    {
        var result = await _systemSettingsService.GetContainerLogsAsync(lines, cancellationToken);

        if (!result.Success || result.Data == null)
        {
            return ApiActionResult<ContainerLogsResponse>.Fail(
                StatusCodes.Status500InternalServerError,
                result.ErrorMessage ?? "Failed to get container logs");
        }

        return ApiActionResult<ContainerLogsResponse>.Ok(new ContainerLogsResponse
        {
            WebLogs = result.Data.WebLogs ?? "No logs available",
            ApiLogs = result.Data.ApiLogs ?? "No logs available",
            PostgresLogs = result.Data.PostgresLogs ?? "No logs available"
        });
    }

    private static SystemSettingsDto MapSettings(Core.Entities.SystemSettings settings)
    {
        return new SystemSettingsDto
        {
            HostCraftDomain = settings.HostCraftDomain,
            HostCraftApiDomain = settings.HostCraftApiDomain,
            HostCraftEnableHttps = settings.HostCraftEnableHttps,
            HostCraftLetsEncryptEmail = settings.HostCraftLetsEncryptEmail,
            CertificateStatus = settings.CertificateStatus,
            ConfiguredAt = settings.ConfiguredAt,
            ProxyUpdatedAt = settings.ProxyUpdatedAt,
            TraefikDashboardDomain = settings.TraefikDashboardDomain,
            TraefikDashboardAuthEnabled = settings.TraefikDashboardAuthEnabled,
            TraefikDashboardUsername = settings.TraefikDashboardUsername
        };
    }
}
