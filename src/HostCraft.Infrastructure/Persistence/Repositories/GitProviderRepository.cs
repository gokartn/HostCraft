using HostCraft.Core.Entities;
using HostCraft.Core.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace HostCraft.Infrastructure.Persistence.Repositories;

public class GitProviderRepository : IGitProviderRepository
{
    private readonly HostCraftDbContext _context;

    public GitProviderRepository(HostCraftDbContext context)
    {
        _context = context;
    }

    public async Task<List<GitProvider>> GetByUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        return await _context.GitProviders
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.ConnectedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<GitProvider?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.GitProviders.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task AddAsync(GitProvider provider, CancellationToken cancellationToken = default)
    {
        _context.GitProviders.Add(provider);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(GitProvider provider, CancellationToken cancellationToken = default)
    {
        _context.GitProviders.Update(provider);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(GitProvider provider, CancellationToken cancellationToken = default)
    {
        _context.GitProviders.Remove(provider);
        await _context.SaveChangesAsync(cancellationToken);
    }
}