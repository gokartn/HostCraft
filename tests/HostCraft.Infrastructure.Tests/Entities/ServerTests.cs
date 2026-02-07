using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using Xunit;

namespace HostCraft.Infrastructure.Tests.Entities;

public class ServerTests
{
    [Fact]
    public void Server_ShouldInitializeWithDefaults()
    {
        // Arrange & Act
        var server = new Server { Name = "test", Host = "localhost" };

        // Assert
        Assert.Equal(ServerStatus.Offline, server.Status);
        Assert.Equal(22, server.Port);
        Assert.Equal(ServerType.Standalone, server.Type);
        Assert.Equal(ProxyType.None, server.ProxyType);
        Assert.Equal("root", server.Username);
        Assert.Equal(ServerStatus.Offline, server.Status);
        Assert.Equal(DateTime.MinValue, server.CreatedAt); // Not set until saved via EF Core
        Assert.Equal(0, server.ConsecutiveFailures);
    }

    [Fact]
    public void Server_IsSwarm_ShouldReturnTrueForSwarmManager()
    {
        // Arrange
        var server = new Server { Name = "manager", Host = "localhost", Type = ServerType.SwarmManager };

        // Act & Assert
        Assert.True(server.IsSwarm);
    }

    [Fact]
    public void Server_IsSwarm_ShouldReturnTrueForSwarmWorker()
    {
        // Arrange
        var server = new Server { Name = "worker", Host = "localhost", Type = ServerType.SwarmWorker };

        // Act & Assert
        Assert.True(server.IsSwarm);
    }

    [Fact]
    public void Server_IsSwarm_ShouldReturnFalseForStandalone()
    {
        // Arrange
        var server = new Server { Name = "standalone", Host = "localhost", Type = ServerType.Standalone };

        // Act & Assert
        Assert.False(server.IsSwarm);
    }

    [Fact]
    public void Server_WithAllProperties_ShouldStoreCorrectly()
    {
        // Arrange & Act
        var server = new Server
        {
            Name = "test-server",
            Host = "192.168.1.100",
            Port = 2222,
            Type = ServerType.SwarmManager,
            Status = ServerStatus.Online,
            ProxyType = ProxyType.Traefik
        };

        // Assert
        Assert.Equal("test-server", server.Name);
        Assert.Equal("192.168.1.100", server.Host);
        Assert.Equal(2222, server.Port);
        Assert.Equal(ServerType.SwarmManager, server.Type);
        Assert.Equal(ServerStatus.Online, server.Status);
        Assert.Equal(ProxyType.Traefik, server.ProxyType);
    }

    [Fact]
    public void Server_RelationshipWithRegion_ShouldBeNullable()
    {
        // Arrange
        var server = new Server { Name = "test", Host = "localhost" };

        // Assert
        Assert.Null(server.RegionId);
    }

    [Fact]
    public void Server_RelationshipWithPrivateKey_ShouldBeNullable()
    {
        // Arrange
        var server = new Server { Name = "test", Host = "localhost" };

        // Assert
        Assert.Null(server.PrivateKeyId);
        Assert.Null(server.PrivateKey);
    }

    [Fact]
    public void Server_ConsecutiveFailures_ShouldDefaultToZero()
    {
        // Arrange & Act
        var server = new Server { Name = "test", Host = "localhost" };

        // Assert
        Assert.Equal(0, server.ConsecutiveFailures);
    }

    [Fact]
    public void Server_SwarmManagerCount_ShouldBeNullableAndDefault()
    {
        // Arrange & Act
        var server = new Server { Name = "test", Host = "localhost" };

        // Assert
        Assert.Null(server.SwarmManagerCount);
    }
}
