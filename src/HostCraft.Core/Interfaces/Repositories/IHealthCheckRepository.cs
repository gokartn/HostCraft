using HostCraft.Core.Entities;
using HostCraft.Core.Enums;

namespace HostCraft.Core.Interfaces.Repositories;

public interface IHealthCheckRepository
{
    Task<List<HealthCheck>> GetByServerTypeInRangeAsync(ServerType serverType, DateTime start, DateTime end, CancellationToken cancellationToken = default);
}