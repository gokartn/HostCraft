using HostCraft.Core.Entities;

namespace HostCraft.Core.Interfaces.Repositories;

/// <summary>
/// Repository interface for AuditLog entity operations
/// </summary>
public interface IAuditLogRepository
{
    Task AddAsync(AuditLog auditLog, CancellationToken cancellationToken = default);
    Task<IEnumerable<AuditLog>> GetPagedAsync(int page, int pageSize, CancellationToken cancellationToken = default);
}
