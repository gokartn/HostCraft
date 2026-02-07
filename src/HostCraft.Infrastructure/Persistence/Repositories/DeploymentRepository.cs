using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using HostCraft.Core.Interfaces.Repositories;
using HostCraft.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HostCraft.Infrastructure.Persistence.Repositories;

/// <summary>
/// Repository implementation for Deployment entity
/// </summary>
public class DeploymentRepository : IDeploymentRepository
{
    private readonly HostCraftDbContext _context;

    public DeploymentRepository(HostCraftDbContext context)
    {
        _context = context;
    }

    public async Task<Deployment?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Deployments
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
    }

    public async Task<Deployment?> GetByIdWithApplicationAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Deployments
            .Include(d => d.Application)
                .ThenInclude(a => a.Server)
                    .ThenInclude(s => s.PrivateKey)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
    }

    public async Task<Deployment?> GetByIdWithApplicationAndGitProviderAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Deployments
            .Include(d => d.Application)
                .ThenInclude(a => a.Server)
                    .ThenInclude(s => s.PrivateKey)
            .Include(d => d.Application)
                .ThenInclude(a => a.GitProvider)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
    }

    public async Task<Deployment?> GetByIdWithApplicationAndLogsAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Deployments
            .Include(d => d.Application)
                .ThenInclude(a => a.Server)
            .Include(d => d.Logs.OrderBy(l => l.Timestamp))
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
    }

    public async Task<Deployment?> GetByIdWithApplicationDetailsAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Deployments
            .Include(d => d.Application)
                .ThenInclude(a => a.Server)
                    .ThenInclude(s => s.PrivateKey)
            .Include(d => d.Application)
                .ThenInclude(a => a.Deployments)
            .Include(d => d.Application)
                .ThenInclude(a => a.GitProvider)
            .Include(d => d.Application)
                .ThenInclude(a => a.Domains)
            .Include(d => d.Application)
                .ThenInclude(a => a.Project)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<Deployment>> GetDeploymentsAsync(int? applicationId, DeploymentStatus? status, int limit, CancellationToken cancellationToken = default)
    {
        var query = _context.Deployments
            .Include(d => d.Application)
                .ThenInclude(a => a.Server)
            .AsQueryable();

        if (applicationId.HasValue)
        {
            query = query.Where(d => d.ApplicationId == applicationId.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(d => d.Status == status.Value);
        }

        return await query
            .OrderByDescending(d => d.StartedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Deployment>> GetPreviewDeploymentsAsync(int applicationId, string previewId, CancellationToken cancellationToken = default)
    {
        return await _context.Deployments
            .Where(d => d.ApplicationId == applicationId && d.IsPreview && d.PreviewId == previewId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<DeploymentLog>> GetLogsAfterAsync(int deploymentId, int afterId, CancellationToken cancellationToken = default)
    {
        return await _context.DeploymentLogs
            .Where(l => l.DeploymentId == deploymentId && l.Id > afterId)
            .OrderBy(l => l.Timestamp)
            .ToListAsync(cancellationToken);
    }

    public async Task<Deployment> AddAsync(Deployment deployment, CancellationToken cancellationToken = default)
    {
        _context.Deployments.Add(deployment);
        await _context.SaveChangesAsync(cancellationToken);
        return deployment;
    }

    public async Task UpdateAsync(Deployment deployment, CancellationToken cancellationToken = default)
    {
        _context.Deployments.Update(deployment);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateRangeAsync(IEnumerable<Deployment> deployments, CancellationToken cancellationToken = default)
    {
        _context.Deployments.UpdateRange(deployments);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
