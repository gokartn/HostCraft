using HostCraft.Api.Services;
using HostCraft.Core.Interfaces;
using HostCraft.Core.Models;
using Microsoft.AspNetCore.Http;
using Moq;
using Xunit;

namespace HostCraft.Api.Tests.Services;

public class HADashboardWorkflowServiceTests
{
    private readonly Mock<IDashboardService> _dashboardService = new();
    private readonly HADashboardWorkflowService _workflowService;

    public HADashboardWorkflowServiceTests()
    {
        _workflowService = new HADashboardWorkflowService(_dashboardService.Object);
    }

    [Fact]
    public async Task GetClusterStatusAsync_ReturnsOkWithData()
    {
        var expected = CreateClusterStatus();
        _dashboardService
            .Setup(s => s.GetClusterStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await _workflowService.GetClusterStatusAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.Same(expected, result.Data);
        _dashboardService.Verify(s => s.GetClusterStatusAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetHistoryAsync_ReturnsOkWithData()
    {
        var history = CreateHistory();
        _dashboardService
            .Setup(s => s.GetHistoryAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(history);

        var result = await _workflowService.GetHistoryAsync(24, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.Same(history, result.Data);
        _dashboardService.Verify(s => s.GetHistoryAsync(24, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetNodeMetricsAsync_WhenMetricsMissing_Returns404()
    {
        _dashboardService
            .Setup(s => s.GetNodeMetricsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HANodeMetricsDto?)null);

        var result = await _workflowService.GetNodeMetricsAsync(5, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(StatusCodes.Status404NotFound, result.StatusCode);
        Assert.NotNull(result.Error);
        Assert.Null(result.Data);
        _dashboardService.Verify(s => s.GetNodeMetricsAsync(5, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetNodeMetricsAsync_WhenMetricsFound_ReturnsOk()
    {
        var metrics = new HANodeMetricsDto("node-1", 5, 12.5, 1_000_000_000, 2_000_000_000, 500_000_000, 1_000_000_000, DateTime.UtcNow);
        _dashboardService
            .Setup(s => s.GetNodeMetricsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(metrics);

        var result = await _workflowService.GetNodeMetricsAsync(5, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.Same(metrics, result.Data);
        _dashboardService.Verify(s => s.GetNodeMetricsAsync(5, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static HAClusterStatusDto CreateClusterStatus()
    {
        return new HAClusterStatusDto(
            "cluster-1",
            3,
            3,
            2,
            2,
            true,
            "leader-1",
            "manager-1",
            "healthy",
            new List<HANodeDto>(),
            new List<HARegionDto>(),
            new List<HAServiceStatusDto>(),
            new List<string>(),
            DateTime.UtcNow);
    }

    private static HAHistoricalDataDto CreateHistory()
    {
        var points = new List<HAMetricPoint> { new(DateTime.UtcNow, 1, "ok") };
        return new HAHistoricalDataDto(points, points, points, points, points, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow, 5);
    }
}
