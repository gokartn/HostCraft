using HostCraft.Core.Entities;
using HostCraft.Core.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace HostCraft.Infrastructure.Persistence.Repositories;

/// <summary>
/// Repository implementation for Domain entity
/// </summary>
public class DomainRepository : IDomainRepository
{
    private readonly HostCraftDbContext _context;

    public DomainRepository(HostCraftDbContext context)
    {
        _context = context;
    }

    public async Task<Domain?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Domains
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
    }

    public async Task<Domain?> GetByIdWithApplicationAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Domains
            .AsNoTrackingWithIdentityResolution()
            .Include(d => d.Application)
                .ThenInclude(a => a.Server)
                    .ThenInclude(s => s.PrivateKey)
            .Include(d => d.Application.Domains)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<Domain>> GetByApplicationIdAsync(int applicationId, CancellationToken cancellationToken = default)
    {
        return await _context.Domains
            .AsNoTracking()
            .Where(d => d.ApplicationId == applicationId)
            .OrderByDescending(d => d.IsPrimary)
            .ThenBy(d => d.Host)
            .ToListAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<Domain> Items, int TotalCount)> GetByApplicationIdPagedAsync(int applicationId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = _context.Domains
            .AsNoTracking()
            .Where(d => d.ApplicationId == applicationId);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(d => d.IsPrimary)
            .ThenBy(d => d.Host)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<Domain?> GetByHostAndPathAsync(string host, string path, CancellationToken cancellationToken = default)
    {
        var normalizedHost = host.ToLowerInvariant().Trim();
        var normalizedPath = path.Trim();
        
        return await _context.Domains
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Host == normalizedHost && d.Path == normalizedPath, cancellationToken);
    }

    public async Task<IEnumerable<Domain>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Domains
            .AsNoTracking()
            .Include(d => d.Application)
            .ToListAsync(cancellationToken);
    }

    public async Task<Domain> AddAsync(Domain domain, CancellationToken cancellationToken = default)
    {
        _context.Domains.Add(domain);
        await _context.SaveChangesAsync(cancellationToken);
        return domain;
    }

    public async Task UpdateAsync(Domain domain, CancellationToken cancellationToken = default)
    {
        domain.UpdatedAt = DateTime.UtcNow;
        _context.Domains.Update(domain);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Domain domain, CancellationToken cancellationToken = default)
    {
        _context.Domains.Remove(domain);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Domains
            .AnyAsync(d => d.Id == id, cancellationToken);
    }

    public async Task<bool> ExistsByHostAndPathAsync(string host, string path, CancellationToken cancellationToken = default)
    {
        var normalizedHost = host.ToLowerInvariant().Trim();
        var normalizedPath = path.Trim();
        
        return await _context.Domains
            .AsNoTracking()
            .AnyAsync(d => d.Host == normalizedHost && d.Path == normalizedPath, cancellationToken);
    }
}
