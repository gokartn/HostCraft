using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HostCraft.Core.Entities;

namespace HostCraft.Core.Interfaces.Repositories;

/// <summary>
/// Repository interface for Compose environment variables.
/// </summary>
public interface IComposeEnvironmentVariableRepository
{
    Task AddRangeAsync(IEnumerable<ComposeEnvironmentVariable> variables, CancellationToken cancellationToken = default);
}
