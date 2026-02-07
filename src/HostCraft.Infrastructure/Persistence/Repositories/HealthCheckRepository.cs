using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using HostCraft.Core.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace HostCraft.Infrastructure.Persistence.Repositories;

public class HealthCheckRepository : IHealthCheckRepository
{
    private readonly HostCraftDbContext _context;

    public HealthCheckRepository(HostCraftDbContext context)
    {
        _context = context;
    }

    public async Task<List<HealthCheck>> GetByServerTypeInRangeAsync(ServerType serverType, DateTime start, DateTime end, CancellationToken cancellationToken = default)
    {
        return await _context.HealthChecks
            .Include(h => h.Server)
            .Where(h => h.CheckedAt >= start && h.CheckedAt <= end)
            .Where(h => h.Server != null && h.Server.Type == serverType)
            .OrderBy(h => h.CheckedAt)
            .ToListAsync(cancellationToken);
    }
}