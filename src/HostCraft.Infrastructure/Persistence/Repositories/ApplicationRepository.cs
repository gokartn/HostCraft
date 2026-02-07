using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HostCraft.Core.Entities;
using HostCraft.Core.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace HostCraft.Infrastructure.Persistence.Repositories;

/// <summary>
/// Repository implementation for Application entity
/// </summary>
public class ApplicationRepository : IApplicationRepository
{
    private readonly HostCraftDbContext _context;

    public ApplicationRepository(HostCraftDbContext context)
    {
        _context = context;
    }

    public async Task<Application?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Applications
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public async Task<Application?> GetByIdWithServerAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Applications
            .AsNoTrackingWithIdentityResolution()
            .Include(a => a.Server)
                .ThenInclude(s => s.PrivateKey)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public async Task<Application?> GetByIdWithServerAndDomainsAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Applications
            .AsNoTrackingWithIdentityResolution()
            .Include(a => a.Server)
                .ThenInclude(s => s.PrivateKey)
            .Include(a => a.Domains)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public async Task<Application?> GetByIdWithServerAndEnvironmentAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Applications
            .Include(a => a.Server)
                .ThenInclude(s => s.PrivateKey)
            .Include(a => a.EnvironmentVariables)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public async Task<Application?> GetByIdWithServerAndDeploymentsAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Applications
            .AsNoTrackingWithIdentityResolution()
            .Include(a => a.Server)
                .ThenInclude(s => s.PrivateKey)
            .Include(a => a.Deployments
                .OrderByDescending(d => d.StartedAt)
                .Take(20))
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public async Task<Application?> GetByIdWithServerProjectAndDeploymentsAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Applications
            .AsNoTrackingWithIdentityResolution()
            .Include(a => a.Server)
                .ThenInclude(s => s.PrivateKey)
            .Include(a => a.Project)
            .Include(a => a.Deployments
                .OrderByDescending(d => d.StartedAt)
                .Take(20))
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public async Task<Application?> GetByUuidWithGitProviderAndServerAsync(Guid uuid, CancellationToken cancellationToken = default)
    {
        return await _context.Applications
            .AsNoTrackingWithIdentityResolution()
            .Include(a => a.GitProvider)
            .Include(a => a.Server)
                .ThenInclude(s => s.PrivateKey)
            .FirstOrDefaultAsync(a => a.Uuid == uuid, cancellationToken);
    }

    public async Task<Application?> GetByIdWithAllRelationsAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Applications
            .AsNoTrackingWithIdentityResolution()
            .Include(a => a.Server)
                .ThenInclude(s => s.PrivateKey)
            .Include(a => a.Domains)
            .Include(a => a.EnvironmentVariables)
            .Include(a => a.Volumes)
            .Include(a => a.Project)
            .Include(a => a.GitProvider)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<Application>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Applications
            .AsNoTrackingWithIdentityResolution()
            .Include(a => a.Server)
            .Include(a => a.Project)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Application>> GetAllWithServerProjectAndLatestDeploymentAsync(int? serverId = null, int? projectId = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Applications
            .AsNoTrackingWithIdentityResolution()
            .Include(a => a.Server)
            .Include(a => a.Project)
            .Include(a => a.Deployments)
            .AsQueryable();

        if (serverId.HasValue)
        {
            query = query.Where(a => a.ServerId == serverId.Value);
        }

        if (projectId.HasValue)
        {
            query = query.Where(a => a.ProjectId == projectId.Value);
        }

        return await query.ToListAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<Application> Items, int TotalCount)> GetPagedWithServerProjectAndLatestDeploymentAsync(int? serverId, int? projectId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = _context.Applications
            .AsNoTrackingWithIdentityResolution()
            .Include(a => a.Server)
            .Include(a => a.Project)
            .Include(a => a.Deployments)
            .AsQueryable();

        if (serverId.HasValue)
        {
            query = query.Where(a => a.ServerId == serverId.Value);
        }

        if (projectId.HasValue)
        {
            query = query.Where(a => a.ProjectId == projectId.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<IEnumerable<Application>> GetByProjectIdAsync(int projectId, CancellationToken cancellationToken = default)
    {
        return await _context.Applications
            .AsNoTrackingWithIdentityResolution()
            .Include(a => a.Server)
            .Where(a => a.ProjectId == projectId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Application>> GetByServerIdAsync(int serverId, CancellationToken cancellationToken = default)
    {
        return await _context.Applications
            .AsNoTrackingWithIdentityResolution()
            .Include(a => a.Project)
            .Where(a => a.ServerId == serverId)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> CountByServerIdAsync(int serverId, CancellationToken cancellationToken = default)
    {
        return await _context.Applications
            .CountAsync(a => a.ServerId == serverId, cancellationToken);
    }

    public async Task<Application?> GetByServerAndNameAsync(int serverId, string name, CancellationToken cancellationToken = default)
    {
        return await _context.Applications
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.ServerId == serverId && a.Name == name, cancellationToken);
    }

    public async Task<Application> AddAsync(Application application, CancellationToken cancellationToken = default)
    {
        _context.Applications.Add(application);
        await _context.SaveChangesAsync(cancellationToken);
        return application;
    }

    public async Task UpdateAsync(Application application, CancellationToken cancellationToken = default)
    {
        _context.Applications.Update(application);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Application application, CancellationToken cancellationToken = default)
    {
        _context.Applications.Remove(application);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Applications
            .AnyAsync(a => a.Id == id, cancellationToken);
    }

    public async Task ReplaceNonSecretEnvironmentVariablesAsync(Application application, IDictionary<string, string> environmentVariables, CancellationToken cancellationToken = default)
    {
        // Normalize incoming keys (case-insensitive) and keep last occurrence to avoid duplicate key constraint hits.
        var normalizedEnv = environmentVariables
            .GroupBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Key: g.Key, Value: g.Last().Value))
            .ToList();

        var existingEnvVars = await _context.EnvironmentVariables
            .Where(ev => ev.ApplicationId == application.Id && !ev.IsSecret)
            .ToListAsync(cancellationToken);

        _context.EnvironmentVariables.RemoveRange(existingEnvVars);

        foreach (var (key, value) in normalizedEnv)
        {
            _context.EnvironmentVariables.Add(new EnvironmentVariable
            {
                ApplicationId = application.Id,
                Key = key,
                Value = value,
                IsSecret = false
            });
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
