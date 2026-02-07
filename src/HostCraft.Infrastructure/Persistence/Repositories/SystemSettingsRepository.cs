using HostCraft.Core.Entities;
using HostCraft.Core.Interfaces.Repositories;

namespace HostCraft.Infrastructure.Persistence.Repositories;

public class SystemSettingsRepository : ISystemSettingsRepository
{
    private readonly HostCraftDbContext _context;

    public SystemSettingsRepository(HostCraftDbContext context)
    {
        _context = context;
    }

    public async Task<SystemSettings?> GetAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SystemSettings.FindAsync([1], cancellationToken);
    }

    public async Task<SystemSettings> GetOrCreateAsync(CancellationToken cancellationToken = default)
    {
        var settings = await GetAsync(cancellationToken);
        if (settings != null)
        {
            return settings;
        }

        settings = new SystemSettings
        {
            Id = 1,
            CreatedAt = DateTime.UtcNow
        };

        _context.SystemSettings.Add(settings);
        await _context.SaveChangesAsync(cancellationToken);
        return settings;
    }

    public async Task UpdateAsync(SystemSettings settings, CancellationToken cancellationToken = default)
    {
        _context.SystemSettings.Update(settings);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
