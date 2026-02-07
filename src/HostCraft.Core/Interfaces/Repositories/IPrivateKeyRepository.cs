using System.Threading;
using System.Threading.Tasks;
using HostCraft.Core.Entities;

namespace HostCraft.Core.Interfaces.Repositories;

/// <summary>
/// Repository interface for PrivateKey entity operations
/// </summary>
public interface IPrivateKeyRepository
{
    Task<PrivateKey> AddAsync(PrivateKey privateKey, CancellationToken cancellationToken = default);
    Task UpdateAsync(PrivateKey privateKey, CancellationToken cancellationToken = default);
    Task DeleteAsync(PrivateKey privateKey, CancellationToken cancellationToken = default);
    Task<PrivateKey?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<PrivateKey?> GetByNameAsync(string name, CancellationToken cancellationToken = default);
    Task<IEnumerable<PrivateKey>> GetAllAsync(CancellationToken cancellationToken = default);
}
