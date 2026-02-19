using Docker.DotNet;
using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using Microsoft.EntityFrameworkCore;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace HostCraft.Infrastructure.Persistence;

public static class DbSeeder
{
    private record DockerSwarmStatus(bool IsAvailable, bool IsSwarmActive, bool IsSwarmManager,
        string? SwarmNodeId = null, string? SwarmId = null, string? SwarmNodeAddress = null,
        string? SwarmNodeState = null, string? SwarmNodeAvailability = null, string? Hostname = null);
    public static async Task SeedAsync(HostCraftDbContext context)
    {
        // NOTE: We no longer seed a default user here.
        // Users must complete the /setup page to create their admin account.
        // This ensures proper password hashing and security.

        // Only ensure localhost server is configured if Docker is available
        await EnsureLocalhostServerAsync(context);

        // Seed database templates so the catalog is not empty
        await SeedDatabaseTemplatesAsync(context);
    }

    private static async Task SeedDatabaseTemplatesAsync(HostCraftDbContext context)
    {
        if (await context.DatabaseTemplates.AnyAsync())
        {
            return;
        }

        var templates = new List<DatabaseTemplate>
        {
            new()
            {
                Name = "PostgreSQL",
                Type = DatabaseType.PostgreSQL,
                Category = "Relational",
                Description = "The World's Most Advanced Open Source Relational Database.",
                DockerImage = "postgres:18-alpine",
                DefaultPort = 5432,
                DefaultVolumePath = "/var/lib/postgresql",
                DefaultEnvironmentVariables = JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    { "POSTGRES_DB", "app" },
                    { "POSTGRES_USER", "postgres" },
                    { "POSTGRES_PASSWORD", "changeme" },
                    { "PGDATA", "/var/lib/postgresql/data/pgdata" }
                }),
                RecommendedMemoryBytes = 512 * 1024 * 1024, // 512MB
                RecommendedCpuLimit = 1.0,
                IconUrl = "https://cdn.jsdelivr.net/gh/devicons/devicon/icons/postgresql/postgresql-original.svg"
            },
            new()
            {
                Name = "MySQL",
                Type = DatabaseType.MySQL,
                Category = "Relational",
                Description = "The world's most popular open source database.",
                DockerImage = "mysql:8.4",
                DefaultPort = 3306,
                DefaultVolumePath = "/var/lib/mysql",
                DefaultEnvironmentVariables = JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    { "MYSQL_DATABASE", "app" },
                    { "MYSQL_USER", "app" },
                    { "MYSQL_PASSWORD", "changeme" },
                    { "MYSQL_ROOT_PASSWORD", "changeme" }
                }),
                RecommendedMemoryBytes = 512 * 1024 * 1024, // 512MB
                RecommendedCpuLimit = 1.0,
                IconUrl = "https://cdn.jsdelivr.net/gh/devicons/devicon/icons/mysql/mysql-original.svg"
            },
            new()
            {
                Name = "MariaDB",
                Type = DatabaseType.MariaDB,
                Category = "Relational",
                Description = "One of the most popular database servers. Made by the original developers of MySQL.",
                DockerImage = "mariadb:11",
                DefaultPort = 3306,
                DefaultVolumePath = "/var/lib/mysql",
                DefaultEnvironmentVariables = JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    { "MARIADB_DATABASE", "app" },
                    { "MARIADB_USER", "app" },
                    { "MARIADB_PASSWORD", "changeme" },
                    { "MARIADB_ROOT_PASSWORD", "changeme" }
                }),
                RecommendedMemoryBytes = 512 * 1024 * 1024,
                RecommendedCpuLimit = 1.0,
                IconUrl = "https://cdn.jsdelivr.net/gh/devicons/devicon/icons/mariadb/mariadb-original.svg"
            },
            new()
            {
                Name = "Redis",
                Type = DatabaseType.Redis,
                Category = "Key-Value",
                Description = "The open source, in-memory data store used by millions of developers as a database, cache, streaming engine, and message broker.",
                DockerImage = "redis:7.2-alpine",
                DefaultPort = 6379,
                DefaultVolumePath = "/data",
                DefaultEnvironmentVariables = JsonSerializer.Serialize(new Dictionary<string, string>
                {
                     // Redis typically uses command args for password, handled separately or via custom command
                }),
                RecommendedMemoryBytes = 256 * 1024 * 1024, // 256MB
                RecommendedCpuLimit = 0.5,
                IconUrl = "https://cdn.jsdelivr.net/gh/devicons/devicon/icons/redis/redis-original.svg"
            },
            new()
            {
                Name = "MongoDB",
                Type = DatabaseType.MongoDB,
                Category = "NoSQL",
                Description = "The application data platform.",
                DockerImage = "mongo:7.0",
                DefaultPort = 27017,
                DefaultVolumePath = "/data",
                DefaultEnvironmentVariables = JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    { "MONGO_INITDB_ROOT_USERNAME", "root" },
                    { "MONGO_INITDB_ROOT_PASSWORD", "changeme" },
                    { "MONGO_INITDB_DATABASE", "app" }
                }),
                RecommendedMemoryBytes = 1024 * 1024 * 1024, // 1GB
                RecommendedCpuLimit = 1.0,
                IconUrl = "https://cdn.jsdelivr.net/gh/devicons/devicon/icons/mongodb/mongodb-original.svg"
            }
        };

        context.DatabaseTemplates.AddRange(templates);
        await context.SaveChangesAsync();
    }
    
    private static async Task EnsureLocalhostServerAsync(HostCraftDbContext context)
    {
        // Check if localhost seeding is explicitly disabled
        var skipLocalhostSeed = Environment.GetEnvironmentVariable("SKIP_LOCALHOST_SEED");
        if (!string.IsNullOrEmpty(skipLocalhostSeed) && skipLocalhostSeed.ToLower() == "true")
        {
            return;
        }

        // Check if localhost server already exists
        var localhostExists = await context.Servers.AnyAsync(s =>
            s.Host == "localhost" || s.Host == "127.0.0.1");

        if (localhostExists)
        {
            return;
        }

        // Detect Docker availability and Swarm status via Docker.DotNet (socket-based).
        // The docker CLI is not available inside the container, but the Docker socket
        // (/var/run/docker.sock) is mounted from the host.
        var status = await DetectDockerSwarmStatusAsync();

        if (!status.IsAvailable)
        {
            return;
        }

        var serverType = status.IsSwarmActive && status.IsSwarmManager
            ? ServerType.SwarmManager
            : status.IsSwarmActive
                ? ServerType.SwarmWorker
                : ServerType.Standalone;

        var localhostServer = new Server
        {
            Name = "Local Server",
            Host = "localhost",
            Port = 22,
            Username = Environment.UserName,
            Status = ServerStatus.Online,
            Type = serverType,
            ProxyType = ProxyType.None,
            RegionId = null,
            PrivateKeyId = null,
            IsSwarmManager = status.IsSwarmActive && status.IsSwarmManager,
            IsSwarmWorker = status.IsSwarmActive && !status.IsSwarmManager,
            ActualHostname = status.Hostname,
            SwarmNodeId = status.SwarmNodeId,
            SwarmId = status.SwarmId,
            SwarmAdvertiseAddress = status.SwarmNodeAddress,
            SwarmNodeState = status.SwarmNodeState,
            SwarmNodeAvailability = status.SwarmNodeAvailability,
            CreatedAt = DateTime.UtcNow
        };

        context.Servers.Add(localhostServer);

        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("IX_Servers_Host_Port") == true)
        {
            // Duplicate server - race condition between API replicas
            // This is expected and safe to ignore
        }
    }

    /// <summary>
    /// Detects Docker availability and Swarm status using Docker.DotNet via the mounted socket.
    /// This works inside containers where the docker CLI is not installed but the socket is mounted.
    /// Falls back to the LOCALHOST_IS_SWARM_MANAGER env var if Docker.DotNet fails.
    /// </summary>
    private static async Task<DockerSwarmStatus> DetectDockerSwarmStatusAsync()
    {
        try
        {
            var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            var dockerUri = isWindows
                ? new Uri("npipe://./pipe/docker_engine")
                : new Uri("unix:///var/run/docker.sock");

            // Quick availability check: on Linux, verify the socket file exists
            if (!isWindows && !File.Exists("/var/run/docker.sock"))
            {
                return FallbackToEnvVar();
            }

            using var client = new DockerClientConfiguration(dockerUri).CreateClient();
            var info = await client.System.GetSystemInfoAsync();

            var swarmActive = info.Swarm?.LocalNodeState == "active";
            var isManager = info.Swarm?.ControlAvailable ?? false;

            string? swarmNodeId = null;
            string? swarmId = null;
            string? swarmNodeAddress = null;
            string? swarmNodeState = null;
            string? swarmNodeAvailability = null;

            if (swarmActive)
            {
                swarmNodeId = info.Swarm?.NodeID;
                swarmNodeAddress = info.Swarm?.NodeAddr;

                try
                {
                    var swarm = await client.Swarm.InspectSwarmAsync();
                    swarmId = swarm?.ID;

                    if (!string.IsNullOrEmpty(swarmNodeId))
                    {
                        var node = await client.Swarm.InspectNodeAsync(swarmNodeId);
                        swarmNodeState = node?.Status?.State?.ToString()?.ToLower();
                        swarmNodeAvailability = node?.Spec?.Availability?.ToLower();
                    }
                }
                catch
                {
                    // Non-critical: swarm metadata is nice-to-have during seeding
                }
            }

            return new DockerSwarmStatus(
                IsAvailable: true,
                IsSwarmActive: swarmActive,
                IsSwarmManager: isManager,
                SwarmNodeId: swarmNodeId,
                SwarmId: swarmId,
                SwarmNodeAddress: swarmNodeAddress,
                SwarmNodeState: swarmNodeState,
                SwarmNodeAvailability: swarmNodeAvailability,
                Hostname: info.Name);
        }
        catch
        {
            return FallbackToEnvVar();
        }
    }

    private static DockerSwarmStatus FallbackToEnvVar()
    {
        var swarmManagerEnv = Environment.GetEnvironmentVariable("LOCALHOST_IS_SWARM_MANAGER");
        var isManager = !string.IsNullOrEmpty(swarmManagerEnv) &&
                        swarmManagerEnv.Equals("true", StringComparison.OrdinalIgnoreCase);
        // If the env var says swarm manager, Docker must be available (the install script set it)
        return new DockerSwarmStatus(IsAvailable: isManager, IsSwarmActive: isManager, IsSwarmManager: isManager);
    }
}
