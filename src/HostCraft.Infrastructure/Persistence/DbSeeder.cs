using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace HostCraft.Infrastructure.Persistence;

public static class DbSeeder
{
    public static async Task SeedAsync(HostCraftDbContext context)
    {
        // Check if already seeded
        if (await context.Regions.AnyAsync() || await context.Projects.AnyAsync())
        {
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
    }
}
