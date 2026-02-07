using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HostCraft.Core.Entities;
using HostCraft.Core.Interfaces.Repositories;
using HostCraft.Infrastructure.Persistence;

namespace HostCraft.Infrastructure.Persistence.Repositories;

/// <summary>
/// Repository implementation for Compose environment variables.
/// </summary>
public class ComposeEnvironmentVariableRepository : IComposeEnvironmentVariableRepository
{
    private readonly HostCraftDbContext _context;

    public ComposeEnvironmentVariableRepository(HostCraftDbContext context)
    {
        _context = context;
    }

    public async Task AddRangeAsync(IEnumerable<ComposeEnvironmentVariable> variables, CancellationToken cancellationToken = default)
    {
        _context.ComposeEnvironmentVariables.AddRange(variables);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
