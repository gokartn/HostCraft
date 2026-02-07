using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HostCraft.Core.Constants;
using HostCraft.Core.Entities;
using HostCraft.Core.Interfaces.Repositories;
using HostCraft.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HostCraft.Infrastructure.Persistence.Repositories;

/// <summary>
/// Repository implementation for Project entity
/// </summary>
public class ProjectRepository : IProjectRepository
{
    private readonly HostCraftDbContext _context;

    public ProjectRepository(HostCraftDbContext context)
    {
        _context = context;
    }

    public async Task<Project?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Projects
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<Project?> GetByIdWithApplicationsAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Projects
            .Include(p => p.Applications)
                .ThenInclude(a => a.Server)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<Project>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Projects
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Project>> GetAllWithApplicationsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Projects
            .Include(p => p.Applications)
            .Where(p => p.Name != SystemProjects.GlobalDeployments)
            .ToListAsync(cancellationToken);
    }

    public async Task<Project?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        var normalized = name.ToLowerInvariant();
        return await _context.Projects
            .FirstOrDefaultAsync(p => p.Name.ToLower() == normalized, cancellationToken);
    }

    public async Task<bool> ExistsByNameAsync(string name, int? excludeId = null, CancellationToken cancellationToken = default)
    {
        var normalized = name.ToLowerInvariant();
        var query = _context.Projects.Where(p => p.Name.ToLower() == normalized);

        if (excludeId.HasValue)
        {
            query = query.Where(p => p.Id != excludeId.Value);
        }

        return await query.AnyAsync(cancellationToken);
    }

    public async Task<Project> AddAsync(Project project, CancellationToken cancellationToken = default)
    {
        _context.Projects.Add(project);
        await _context.SaveChangesAsync(cancellationToken);
        return project;
    }

    public async Task UpdateAsync(Project project, CancellationToken cancellationToken = default)
    {
        _context.Projects.Update(project);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Project project, CancellationToken cancellationToken = default)
    {
        _context.Projects.Remove(project);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<Project> GetOrCreateGlobalAsync(string description, CancellationToken cancellationToken = default)
    {
        var project = await _context.Projects
            .Include(p => p.Applications)
            .FirstOrDefaultAsync(p => p.Name == SystemProjects.GlobalDeployments, cancellationToken);

        if (project != null)
            return project;

        project = new Project
        {
            Uuid = Guid.NewGuid(),
            Name = SystemProjects.GlobalDeployments,
            Description = description,
            CreatedAt = DateTime.UtcNow
        };

        _context.Projects.Add(project);
        await _context.SaveChangesAsync(cancellationToken);

        return project;
    }
}
