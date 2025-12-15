using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using Microsoft.EntityFrameworkCore;
using System.Runtime.InteropServices;

namespace HostCraft.Infrastructure.Persistence;

public static class DbSeeder
{
    public static async Task SeedAsync(HostCraftDbContext context)
    {
        // Check if already seeded
        if (await context.Regions.AnyAsync() || await context.Projects.AnyAsync())
        {
            // Still check for localhost server even if already seeded
            await EnsureLocalhostServerAsync(context);
            return;
        }

        // Seed Regions
        var regions = new[]
        {
            new Region { Name = "US East", Code = "us-east-1", IsPrimary = true, Priority = 1 },
            new Region { Name = "EU West", Code = "eu-west-1", IsPrimary = false, Priority = 2 },
        };
        context.Regions.AddRange(regions);
        await context.SaveChangesAsync();

        // Seed sample projects
        var project = new Project
        {
            Name = "Demo Project",
            Description = "Sample project for testing"
        };
        context.Projects.Add(project);
        await context.SaveChangesAsync();
        
        // Ensure localhost server is configured
        await EnsureLocalhostServerAsync(context);
    }
    
    private static async Task EnsureLocalhostServerAsync(HostCraftDbContext context)
    {
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
        
        // Get primary region
        var primaryRegion = await context.Regions.FirstOrDefaultAsync(r => r.IsPrimary);
        
        if (primaryRegion == null)
        {
            // No region available, skip localhost configuration
            return;
        }
        
        // Create localhost server entry
        var localhostServer = new Server
        {
            Name = "Local Server",
            Host = "localhost",
            Port = 22, // Not actually used for localhost
            Username = Environment.UserName,
            Status = ServerStatus.Online,
            Type = ServerType.Standalone,
            ProxyType = ProxyType.None,
            RegionId = primaryRegion.Id,
            PrivateKeyId = null, // No SSH key needed for localhost
            CreatedAt = DateTime.UtcNow
        };
        
        context.Servers.Add(localhostServer);
        await context.SaveChangesAsync();
    }
    
    private static bool IsDockerAvailable()
    {
        try
        {
            var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            var dockerCommand = isWindows ? "docker" : "docker";
            
            var processStartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = dockerCommand,
                Arguments = "info",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = System.Diagnostics.Process.Start(processStartInfo);
            if (process == null)
            {
                return false;
            }
            
            process.WaitForExit(5000); // 5 second timeout
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
