using System.Threading;
using System.Threading.Tasks;
using HostCraft.Core.Entities;

namespace HostCraft.Core.Interfaces.Repositories;

/// <summary>
/// Repository interface for Region entity operations
/// </summary>
public interface IRegionRepository
{
    Task<Region?> GetByNameOrCodeAsync(string value, CancellationToken cancellationToken = default);
    Task<Region> AddAsync(Region region, CancellationToken cancellationToken = default);
    Task<Region?> GetPrimaryAsync(CancellationToken cancellationToken = default);
}
