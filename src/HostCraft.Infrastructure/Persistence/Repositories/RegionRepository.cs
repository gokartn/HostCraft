using System.Threading;
using System.Threading.Tasks;
using HostCraft.Core.Entities;
using HostCraft.Core.Interfaces.Repositories;
using HostCraft.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HostCraft.Infrastructure.Persistence.Repositories;

/// <summary>
/// Repository implementation for Region entity
/// </summary>
public class RegionRepository : IRegionRepository
{
    private readonly HostCraftDbContext _context;

    public RegionRepository(HostCraftDbContext context)
    {
        _context = context;
    }

    public async Task<Region?> GetByNameOrCodeAsync(string value, CancellationToken cancellationToken = default)
    {
        return await _context.Regions
            .FirstOrDefaultAsync(r => r.Name == value || r.Code == value, cancellationToken);
    }

    public async Task<Region?> GetPrimaryAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Regions
            .FirstOrDefaultAsync(r => r.IsPrimary, cancellationToken);
    }

    public async Task<Region> AddAsync(Region region, CancellationToken cancellationToken = default)
    {
        _context.Regions.Add(region);
        await _context.SaveChangesAsync(cancellationToken);
        return region;
    }
}
