using HostCraft.Core.Entities;
using HostCraft.Core.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace HostCraft.Infrastructure.Persistence.Repositories;

public class CertificateRepository : ICertificateRepository
{
    private readonly HostCraftDbContext _context;

    public CertificateRepository(HostCraftDbContext context)
    {
        _context = context;
    }

    public async Task<List<Certificate>> GetByApplicationIdAsync(int applicationId, CancellationToken cancellationToken = default)
    {
        return await _context.Certificates
            .Where(c => c.ApplicationId == applicationId)
            .ToListAsync(cancellationToken);
    }

    public async Task<Certificate?> GetByIdAndApplicationAsync(int certificateId, int applicationId, CancellationToken cancellationToken = default)
    {
        return await _context.Certificates
            .FirstOrDefaultAsync(c => c.Id == certificateId && c.ApplicationId == applicationId, cancellationToken);
    }

    public async Task DeleteAsync(Certificate certificate, CancellationToken cancellationToken = default)
    {
        _context.Certificates.Remove(certificate);
        await _context.SaveChangesAsync(cancellationToken);
    }
}