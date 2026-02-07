using HostCraft.Core.Entities;
using HostCraft.Core.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace HostCraft.Infrastructure.Persistence.Repositories;

/// <summary>
/// Repository implementation for EnvironmentVariable entity
/// </summary>
public class EnvironmentVariableRepository : IEnvironmentVariableRepository
{
    private readonly HostCraftDbContext _context;

    public EnvironmentVariableRepository(HostCraftDbContext context)
    {
        _context = context;
    }

    public async Task<EnvironmentVariable?> GetByApplicationAndKeyAsync(int applicationId, string key, CancellationToken cancellationToken = default)
    {
        return await _context.EnvironmentVariables
            .FirstOrDefaultAsync(e => e.ApplicationId == applicationId && e.Key == key, cancellationToken);
    }

    public async Task<IEnumerable<EnvironmentVariable>> GetByApplicationAsync(int applicationId, CancellationToken cancellationToken = default)
    {
        return await _context.EnvironmentVariables
            .Where(e => e.ApplicationId == applicationId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<EnvironmentVariable>> GetSecretsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.EnvironmentVariables
            .Where(e => e.IsSecret)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(EnvironmentVariable environmentVariable, CancellationToken cancellationToken = default)
    {
        _context.EnvironmentVariables.Add(environmentVariable);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(EnvironmentVariable environmentVariable, CancellationToken cancellationToken = default)
    {
        _context.EnvironmentVariables.Update(environmentVariable);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(EnvironmentVariable environmentVariable, CancellationToken cancellationToken = default)
    {
        _context.EnvironmentVariables.Remove(environmentVariable);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
