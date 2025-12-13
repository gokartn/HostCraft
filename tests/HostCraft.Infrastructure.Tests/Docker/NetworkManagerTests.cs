using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using HostCraft.Infrastructure.Docker;
using Moq;
using HostCraft.Core.Interfaces;
using Xunit;

namespace HostCraft.Infrastructure.Tests.Docker;

public class NetworkManagerTests
{
    private readonly NetworkManager _networkManager;
    private readonly Mock<IDockerService> _dockerServiceMock;

    public NetworkManagerTests()
    {
        _dockerServiceMock = new Mock<IDockerService>();
        _networkManager = new NetworkManager(_dockerServiceMock.Object);
    }

    [Fact]
    public void GetRequiredNetworkType_ShouldReturnOverlay_WhenServerIsSwarmManager()
    {
        // Arrange
        var swarmManager = new Server { Name = "manager", Host = "localhost", Type = ServerType.SwarmManager };

        // Act
        var networkType = _networkManager.GetRequiredNetworkType(swarmManager);

        // Assert
        Assert.Equal(NetworkType.Overlay, networkType);
    }

    [Fact]
    public void GetRequiredNetworkType_ShouldReturnOverlay_WhenServerIsSwarmWorker()
    {
        // Arrange
        var swarmWorker = new Server { Name = "worker", Host = "localhost", Type = ServerType.SwarmWorker };

        // Act
        var networkType = _networkManager.GetRequiredNetworkType(swarmWorker);

        // Assert
        Assert.Equal(NetworkType.Overlay, networkType);
    }

    [Fact]
    public void GetRequiredNetworkType_ShouldReturnBridge_WhenServerIsStandalone()
    {
        // Arrange
        var standaloneServer = new Server { Name = "standalone", Host = "localhost", Type = ServerType.Standalone };

        // Act
        var networkType = _networkManager.GetRequiredNetworkType(standaloneServer);

        // Assert
        Assert.Equal(NetworkType.Bridge, networkType);
    }

    [Theory]
    [InlineData(ServerType.SwarmManager, NetworkType.Overlay)]
    [InlineData(ServerType.SwarmWorker, NetworkType.Overlay)]
    [InlineData(ServerType.Standalone, NetworkType.Bridge)]
    public void GetRequiredNetworkType_ShouldReturnCorrectType_ForAllServerTypes(
        ServerType serverType,
        NetworkType expectedNetworkType)
    {
        // Arrange
        var server = new Server { Name = "test", Host = "localhost", Type = serverType };

        // Act
        var result = _networkManager.GetRequiredNetworkType(server);

        // Assert
        Assert.Equal(expectedNetworkType, result);
    }

    [Fact]
    public void NetworkTypeValidation_IntegrationScenario_CoolifyBugFix()
    {
        // This test documents the exact bug we're fixing from Coolify
        // Coolify creates bridge network but requires overlay for Swarm

        // Arrange
        var swarmServer = new Server
        {
            Name = "production-swarm",
            Host = "10.0.1.10",
            Type = ServerType.SwarmManager
        };

        // Coolify incorrectly creates bridge network
        var coolifyCreatedNetworkType = NetworkType.Bridge; // WRONG!
        var correctNetworkType = _networkManager.GetRequiredNetworkType(swarmServer);

        // Assert - Our logic returns the correct type
        Assert.Equal(NetworkType.Overlay, correctNetworkType);
        Assert.NotEqual(correctNetworkType, coolifyCreatedNetworkType);
    }
}
