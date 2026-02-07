using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using Xunit;

namespace HostCraft.Infrastructure.Tests.Entities;

public class ApplicationTests
{
    [Fact]
    public void Application_ShouldInitializeWithDefaults()
    {
        // Arrange & Act
        var app = new Application { Name = "test-app" };

        // Assert
        Assert.Equal(ApplicationSourceType.DockerImage, app.SourceType);
        Assert.Equal(1, app.Replicas);
        Assert.Equal(DateTime.MinValue, app.CreatedAt); // Not set until saved via EF Core
        Assert.Equal(60, app.HealthCheckIntervalSeconds);
        Assert.Equal(10, app.HealthCheckTimeoutSeconds);
        Assert.Equal(3, app.MaxConsecutiveFailures);
        Assert.True(app.AutoRestart);
        Assert.True(app.AutoRollback);
        Assert.Equal(30, app.BackupRetentionDays);
    }

    [Fact]
    public void Application_WithDockerImage_ShouldStoreCorrectly()
    {
        // Arrange & Act
        var app = new Application
        {
            Name = "my-app",
            SourceType = ApplicationSourceType.DockerImage,
            DockerImage = "nginx:latest",
            Replicas = 3
        };

        // Assert
        Assert.Equal("my-app", app.Name);
        Assert.Equal(ApplicationSourceType.DockerImage, app.SourceType);
        Assert.Equal("nginx:latest", app.DockerImage);
        Assert.Equal(3, app.Replicas);
    }

    [Fact]
    public void Application_Relationships_ShouldBeNavigable()
    {
        // Arrange
        var server = new Server { Id = 1, Name = "test-server", Host = "localhost" };
        var project = new Project { Id = 1, Name = "test-project" };
        var app = new Application
        {
            Name = "test-app",
            ServerId = 1,
            ProjectId = 1
        };

        // Act
        app.Server = server;
        app.Project = project;

        // Assert
        Assert.NotNull(app.Server);
        Assert.Equal("test-server", app.Server.Name);
        Assert.NotNull(app.Project);
        Assert.Equal("test-project", app.Project.Name);
    }
}
