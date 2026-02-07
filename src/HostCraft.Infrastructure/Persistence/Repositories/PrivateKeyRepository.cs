using System.Threading;
using System.Threading.Tasks;
using HostCraft.Core.Entities;
using HostCraft.Core.Interfaces.Repositories;
using HostCraft.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HostCraft.Infrastructure.Persistence.Repositories;

/// <summary>
/// Repository implementation for PrivateKey entity
/// </summary>
public class PrivateKeyRepository : IPrivateKeyRepository
{
    private readonly HostCraftDbContext _context;

    public PrivateKeyRepository(HostCraftDbContext context)
    {
        _context = context;
    }

    public async Task<PrivateKey> AddAsync(PrivateKey privateKey, CancellationToken cancellationToken = default)
    {
        _context.PrivateKeys.Add(privateKey);
        await _context.SaveChangesAsync(cancellationToken);
        return privateKey;
    }

    public async Task UpdateAsync(PrivateKey privateKey, CancellationToken cancellationToken = default)
    {
        _context.PrivateKeys.Update(privateKey);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(PrivateKey privateKey, CancellationToken cancellationToken = default)
    {
        _context.PrivateKeys.Remove(privateKey);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<PrivateKey?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.PrivateKeys.FirstOrDefaultAsync(k => k.Id == id, cancellationToken);
    }

    public async Task<PrivateKey?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        return await _context.PrivateKeys.FirstOrDefaultAsync(k => k.Name == name, cancellationToken);
    }

    public async Task<IEnumerable<PrivateKey>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.PrivateKeys.ToListAsync(cancellationToken);
    }
}
