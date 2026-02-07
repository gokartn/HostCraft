using HostCraft.Api.Models.SystemSettings;
using HostCraft.Api.Services;
using HostCraft.Core.Entities;
using HostCraft.Core.Interfaces;
using HostCraft.Core.Models.Results;
using HostCraft.Core.Models.SystemSettings;
using Microsoft.AspNetCore.Http;
using Moq;
using Xunit;

namespace HostCraft.Api.Tests.Services;

public class SystemSettingsWorkflowServiceTests
{
    private readonly Mock<ISystemSettingsService> _systemSettingsService = new();
    private readonly SystemSettingsWorkflowService _workflowService;

    public SystemSettingsWorkflowServiceTests()
    {
        _workflowService = new SystemSettingsWorkflowService(_systemSettingsService.Object);
    }

    [Fact]
    public async Task GetSettingsAsync_WhenNoneConfigured_ReturnsDefaults()
    {
        _systemSettingsService
            .Setup(s => s.GetSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((SystemSettings?)null);

        var result = await _workflowService.GetSettingsAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.NotNull(result.Data);
        Assert.True(result.Data!.HostCraftEnableHttps);
        Assert.False(result.Data.TraefikDashboardAuthEnabled);
        Assert.Null(result.Data.HostCraftDomain);
        Assert.Null(result.Data.TraefikDashboardDomain);
    }

    [Fact]
    public async Task GetSettingsAsync_WhenSettingsExist_ReturnsMappedDto()
    {
        var settings = new SystemSettings
        {
            HostCraftDomain = "panel.example.com",
            HostCraftApiDomain = "api.example.com",
            HostCraftEnableHttps = true,
            HostCraftLetsEncryptEmail = "admin@example.com",
            CertificateStatus = "valid",
            ConfiguredAt = DateTime.UtcNow.AddDays(-1),
            ProxyUpdatedAt = DateTime.UtcNow,
            TraefikDashboardDomain = "traefik.example.com",
            TraefikDashboardAuthEnabled = true,
            TraefikDashboardUsername = "admin"
        };

        _systemSettingsService
            .Setup(s => s.GetSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(settings);

        var result = await _workflowService.GetSettingsAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.NotNull(result.Data);
        Assert.Equal(settings.HostCraftDomain, result.Data!.HostCraftDomain);
        Assert.Equal(settings.HostCraftApiDomain, result.Data.HostCraftApiDomain);
        Assert.Equal(settings.HostCraftLetsEncryptEmail, result.Data.HostCraftLetsEncryptEmail);
        Assert.Equal(settings.CertificateStatus, result.Data.CertificateStatus);
        Assert.Equal(settings.TraefikDashboardDomain, result.Data.TraefikDashboardDomain);
        Assert.True(result.Data.TraefikDashboardAuthEnabled);
        Assert.Equal(settings.TraefikDashboardUsername, result.Data.TraefikDashboardUsername);
    }

    [Fact]
    public async Task ConfigureHostCraftAsync_WhenServiceFails_ReturnsFail()
    {
        _systemSettingsService
            .Setup(s => s.ConfigureHostCraftAsync(It.IsAny<ConfigureHostCraftCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult.Failure<SystemSettings>("unable to configure"));

        var request = new ConfigureHostCraftRequest("panel.example.com", "api.example.com", true, "admin@example.com");

        var result = await _workflowService.ConfigureHostCraftAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(StatusCodes.Status500InternalServerError, result.StatusCode);
        Assert.Equal("unable to configure", result.Error);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task ConfigureHostCraftAsync_WhenServiceSucceeds_ReturnsOk()
    {
        _systemSettingsService
            .Setup(s => s.ConfigureHostCraftAsync(It.IsAny<ConfigureHostCraftCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult.Success(new SystemSettings()));

        var request = new ConfigureHostCraftRequest("panel.example.com", "api.example.com", true, "admin@example.com");

        var result = await _workflowService.ConfigureHostCraftAsync(request, CancellationToken.None);

        var expectedMessage = "HostCraft domain configured successfully! Your panel is now accessible at https://panel.example.com. SSL certificate will be automatically provisioned.";
        Assert.True(result.Success);
        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.Equal(expectedMessage, result.Data!.Message);
        Assert.True(result.Data.Success);
        Assert.Equal(request.Domain, result.Data.Domain);
        Assert.True(result.Data.HttpsEnabled);
    }

    [Fact]
    public async Task ConfigureTraefikDashboardAsync_WhenRemovingConfig_ReturnsRemovalMessage()
    {
        _systemSettingsService
            .Setup(s => s.ConfigureTraefikDashboardAsync(It.IsAny<ConfigureTraefikDashboardCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult.Success(new SystemSettings()));

        var request = new ConfigureTraefikDashboardRequest(null, false, null, null);

        var result = await _workflowService.ConfigureTraefikDashboardAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.Equal("Traefik dashboard configuration removed. Dashboard is now only accessible via port 8080.", result.Data!.Message);
        Assert.False(result.Data.AuthEnabled);
        Assert.Null(result.Data.DashboardDomain);
    }

    [Fact]
    public async Task ConfigureTraefikDashboardAsync_WhenConfiguringDomain_ReturnsSuccessMessage()
    {
        _systemSettingsService
            .Setup(s => s.ConfigureTraefikDashboardAsync(It.IsAny<ConfigureTraefikDashboardCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult.Success(new SystemSettings()));

        var request = new ConfigureTraefikDashboardRequest("traefik.example.com", true, "admin", "secret");

        var result = await _workflowService.ConfigureTraefikDashboardAsync(request, CancellationToken.None);

        var expectedMessage = "Traefik dashboard configured successfully! Access it at https://traefik.example.com (Username: admin)";
        Assert.True(result.Success);
        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.Equal(expectedMessage, result.Data!.Message);
        Assert.True(result.Data.AuthEnabled);
        Assert.Equal(request.DashboardDomain, result.Data.DashboardDomain);
    }

    [Fact]
    public async Task ConfigureTraefikDashboardAsync_WhenServiceFails_ReturnsFail()
    {
        _systemSettingsService
            .Setup(s => s.ConfigureTraefikDashboardAsync(It.IsAny<ConfigureTraefikDashboardCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult.Failure<SystemSettings>("unable to configure"));

        var request = new ConfigureTraefikDashboardRequest("traefik.example.com", true, "admin", "secret");

        var result = await _workflowService.ConfigureTraefikDashboardAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(StatusCodes.Status500InternalServerError, result.StatusCode);
        Assert.Equal("unable to configure", result.Error);
    }

    [Fact]
    public async Task GetContainerLogsAsync_WhenServiceFails_ReturnsFail()
    {
        _systemSettingsService
            .Setup(s => s.GetContainerLogsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult.Failure<ContainerLogsResult>("logs unavailable"));

        var result = await _workflowService.GetContainerLogsAsync(50, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(StatusCodes.Status500InternalServerError, result.StatusCode);
        Assert.Equal("logs unavailable", result.Error);
    }

    [Fact]
    public async Task GetContainerLogsAsync_WhenServiceSucceeds_ReturnsLogsWithDefaults()
    {
        var containerLogs = new ContainerLogsResult(null, "api log line", null);
        _systemSettingsService
            .Setup(s => s.GetContainerLogsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult.Success(containerLogs));

        var result = await _workflowService.GetContainerLogsAsync(100, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.Equal("No logs available", result.Data!.WebLogs);
        Assert.Equal("api log line", result.Data.ApiLogs);
        Assert.Equal("No logs available", result.Data.PostgresLogs);
    }
}
