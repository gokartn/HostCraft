using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using Microsoft.EntityFrameworkCore;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace HostCraft.Infrastructure.Persistence;

public static class DbSeeder
{
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
        
        // Check if Docker is available on localhost
        var isDockerAvailable = IsDockerAvailable();
        
        if (!isDockerAvailable)
        {
            // Don't auto-configure if Docker is not available
            return;
        }
        
        // Detect if Docker Swarm is active and if this node is a manager
        // This auto-detection is more reliable than environment variables
        var isSwarmManager = IsSwarmActive() && IsSwarmManager();
        
        // Create localhost server entry
        var localhostServer = new Server
        {
            Name = "Local Server",
            Host = "localhost",
            Port = 22, // Not actually used for localhost
            Username = Environment.UserName,
            Status = ServerStatus.Online,
            Type = isSwarmManager ? ServerType.SwarmManager : ServerType.Standalone,
            ProxyType = ProxyType.None,
            RegionId = null, // No region assigned by default
            PrivateKeyId = null, // No SSH key needed for localhost
            IsSwarmManager = isSwarmManager,
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
            // The unique constraint ensures only one localhost server exists
        }
    }
    
    private static bool IsDockerAvailable()
    {
        try
        {
            // IMPORTANT: When running in container, check if HOST's Docker socket is mounted
            // The mounted /var/run/docker.sock gives us access to the HOST's Docker
            var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            var dockerSocket = isWindows ? "//./pipe/docker_engine" : "/var/run/docker.sock";

            if (isWindows)
            {
                // On Windows, check if named pipe exists (difficult to check directly)
                // Try to run docker command if available
                try
                {
                    var processStartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "docker",
                        Arguments = "info",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var process = System.Diagnostics.Process.Start(processStartInfo);
                    if (process != null)
                    {
                        process.WaitForExit(5000);
                        return process.ExitCode == 0;
                    }
                }
                catch
                {
                    // Fall through to return false
                }
                return false;
            }
            else
            {
                // On Linux/Unix, check if Docker socket file exists
                // If we're in a container, this checks for the MOUNTED socket from host
                // which is exactly what we want - it means we CAN access Docker (the host's)
                return File.Exists(dockerSocket);
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Detects if Docker Swarm is active by running 'docker info' and checking for "Swarm: active"
    /// This is more reliable than environment variables for detecting swarm mode
    /// </summary>
    private static bool IsSwarmActive()
    {
        try
        {
            var processStartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "info --format '{{.Swarm.LocalNodeState}}'",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(processStartInfo);
            if (process != null)
            {
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(5000);

                // Check if swarm is active
                return output.Contains("active", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch
        {
            // Fall through to check env var
        }

        // Fallback: check environment variable
        var swarmManagerEnv = Environment.GetEnvironmentVariable("LOCALHOST_IS_SWARM_MANAGER");
        return !string.IsNullOrEmpty(swarmManagerEnv) && swarmManagerEnv.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if the current node is a swarm manager (vs worker)
    /// </summary>
    private static bool IsSwarmManager()
    {
        try
        {
            var processStartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "info --format '{{.Swarm.ControlAvailable}}'",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(processStartInfo);
            if (process != null)
            {
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(5000);

                // ControlAvailable is true for managers
                return output.Contains("true", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch
        {
            // Fall through
        }

        // Fallback: check environment variable
        var swarmManagerEnv = Environment.GetEnvironmentVariable("LOCALHOST_IS_SWARM_MANAGER");
        return !string.IsNullOrEmpty(swarmManagerEnv) && swarmManagerEnv.Equals("true", StringComparison.OrdinalIgnoreCase);
    }
}
