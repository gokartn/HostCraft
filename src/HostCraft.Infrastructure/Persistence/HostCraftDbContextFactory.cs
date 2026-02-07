using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HostCraft.Infrastructure.Persistence;

public class HostCraftDbContextFactory : IDesignTimeDbContextFactory<HostCraftDbContext>
{
    public HostCraftDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<HostCraftDbContext>();
        
        // Use a temporary connection string for migrations
        // The actual connection string will be used at runtime
        optionsBuilder.UseNpgsql("Host=localhost;Database=hostcraft_temp;Username=postgres;Password=temp");
        
        return new HostCraftDbContext(optionsBuilder.Options);
    }
}
