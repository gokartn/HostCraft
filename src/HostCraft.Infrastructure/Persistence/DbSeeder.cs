using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using Microsoft.EntityFrameworkCore;
using System.Runtime.InteropServices;

namespace HostCraft.Infrastructure.Persistence;

public static class DbSeeder
{
    public static async Task SeedAsync(HostCraftDbContext context)
    {
        // Only ensure localhost server is configured if Docker is available
        await EnsureLocalhostServerAsync(context);
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
        
        // Check if localhost should be configured as swarm manager
        var isSwarmManager = false;
        var swarmManagerEnv = Environment.GetEnvironmentVariable("LOCALHOST_IS_SWARM_MANAGER");
        if (!string.IsNullOrEmpty(swarmManagerEnv) && swarmManagerEnv.ToLower() == "true")
        {
            isSwarmManager = true;
        }
        
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
        await context.SaveChangesAsync();
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
}
