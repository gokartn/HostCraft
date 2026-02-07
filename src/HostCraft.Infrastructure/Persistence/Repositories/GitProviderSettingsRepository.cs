using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using HostCraft.Core.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace HostCraft.Infrastructure.Persistence.Repositories;

public class GitProviderSettingsRepository : IGitProviderSettingsRepository
{
    private readonly HostCraftDbContext _context;

    public GitProviderSettingsRepository(HostCraftDbContext context)
    {
        _context = context;
    }

    public async Task<List<GitProviderSettings>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.GitProviderSettings
            .OrderBy(s => s.Type)
            .ToListAsync(cancellationToken);
    }

    public async Task<GitProviderSettings?> GetByTypeAndApiUrlAsync(GitProviderType type, string? apiUrl, CancellationToken cancellationToken = default)
    {
        return await _context.GitProviderSettings
            .FirstOrDefaultAsync(s => s.Type == type && s.ApiUrl == apiUrl, cancellationToken);
    }

    public async Task<GitProviderSettings> AddAsync(GitProviderSettings settings, CancellationToken cancellationToken = default)
    {
        _context.GitProviderSettings.Add(settings);
        await _context.SaveChangesAsync(cancellationToken);
        return settings;
    }

    public async Task UpdateAsync(GitProviderSettings settings, CancellationToken cancellationToken = default)
    {
        _context.GitProviderSettings.Update(settings);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var settings = await _context.GitProviderSettings.FindAsync([id], cancellationToken);
        if (settings == null)
        {
            return false;
        }

        _context.GitProviderSettings.Remove(settings);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}