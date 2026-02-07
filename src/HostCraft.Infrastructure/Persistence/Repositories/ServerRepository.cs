using System.Linq;
using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using HostCraft.Core.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace HostCraft.Infrastructure.Persistence.Repositories;

/// <summary>
/// Repository implementation for Server entity
/// </summary>
public class ServerRepository : IServerRepository
{
    private readonly HostCraftDbContext _context;

    public ServerRepository(HostCraftDbContext context)
    {
        _context = context;
    }

    public async Task<Server?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Servers
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task<Server?> GetByIdWithPrivateKeyAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Servers
            .AsNoTrackingWithIdentityResolution()
            .Include(s => s.PrivateKey)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task<Server?> GetByIdWithPrivateKeyAndRegionAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Servers
            .AsNoTrackingWithIdentityResolution()
            .Include(s => s.PrivateKey)
            .Include(s => s.Region)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task<Server?> GetByIdWithApplicationsAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Servers
            .AsNoTrackingWithIdentityResolution()
            .Include(s => s.PrivateKey)
            .Include(s => s.Applications)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<Server>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Servers
            .AsNoTrackingWithIdentityResolution()
            .Include(s => s.PrivateKey)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Server>> GetAllWithRegionAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Servers
            .AsNoTrackingWithIdentityResolution()
            .Include(s => s.PrivateKey)
            .Include(s => s.Region)
            .ToListAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<Server> Items, int TotalCount)> GetPagedWithRegionAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = _context.Servers
            .AsNoTrackingWithIdentityResolution()
            .Include(s => s.PrivateKey)
            .Include(s => s.Region)
            .AsQueryable();

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(s => s.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<IEnumerable<Server>> GetSwarmManagersAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Servers
            .AsNoTrackingWithIdentityResolution()
            .Include(s => s.PrivateKey)
            .Where(s => s.Type == ServerType.SwarmManager)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Server>> GetSwarmManagersWithRegionAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Servers
            .AsNoTrackingWithIdentityResolution()
            .Include(s => s.PrivateKey)
            .Include(s => s.Region)
            .Where(s => s.Type == ServerType.SwarmManager)
            .ToListAsync(cancellationToken);
    }

    public async Task<Server?> GetFirstReadyManagerAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Servers
            .AsNoTrackingWithIdentityResolution()
            .Include(s => s.PrivateKey)
            .Where(s => s.IsSwarmManager && s.Status == ServerStatus.Online)
            .OrderBy(s => s.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IEnumerable<Server>> GetSwarmWorkersAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Servers
            .AsNoTrackingWithIdentityResolution()
            .Include(s => s.PrivateKey)
            .Where(s => s.Type == ServerType.SwarmWorker)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Server>> GetNonWorkersWithRegionAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Servers
            .AsNoTrackingWithIdentityResolution()
            .Include(s => s.PrivateKey)
            .Include(s => s.Region)
            .Where(s => s.Type != ServerType.SwarmWorker)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> CountReadyManagersAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Servers
            .CountAsync(s => s.IsSwarmManager && s.SwarmNodeState == "ready", cancellationToken);
    }

    public async Task<Server> AddAsync(Server server, CancellationToken cancellationToken = default)
    {
        _context.Servers.Add(server);
        await _context.SaveChangesAsync(cancellationToken);
        return server;
    }

    public async Task UpdateAsync(Server server, CancellationToken cancellationToken = default)
    {
        _context.Servers.Update(server);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Server server, CancellationToken cancellationToken = default)
    {
        _context.Servers.Remove(server);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Servers
            .AnyAsync(s => s.Id == id, cancellationToken);
    }

    public async Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        return await _context.Servers
            .AnyAsync(s => s.Name == name, cancellationToken);
    }
}
