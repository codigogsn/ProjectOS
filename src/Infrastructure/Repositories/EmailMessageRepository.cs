using Microsoft.EntityFrameworkCore;
using ProjectOS.Application.Interfaces;
using ProjectOS.Domain.Entities;
using ProjectOS.Infrastructure.Persistence;

namespace ProjectOS.Infrastructure.Repositories;

public class EmailMessageRepository : IEmailMessageRepository
{
    private readonly AppDbContext _db;

    public EmailMessageRepository(AppDbContext db) => _db = db;

    public async Task<EmailMessage?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.EmailMessages.FirstOrDefaultAsync(m => m.Id == id, ct);
    }

    public async Task<List<EmailMessage>> GetByProjectIdAsync(Guid projectId, CancellationToken ct = default)
    {
        return await _db.EmailMessages
            .Where(m => m.ProjectId == projectId)
            .OrderByDescending(m => m.SentAtUtc)
            .ToListAsync(ct);
    }

    public async Task<List<EmailMessage>> GetRecentByProjectIdAsync(Guid projectId, int limit = 50, CancellationToken ct = default)
    {
        return await _db.EmailMessages
            .Include(m => m.FromContact)
            .Where(m => m.ProjectId == projectId)
            .OrderByDescending(m => m.SentAtUtc)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<List<EmailMessage>> GetByOrganizationIdAsync(Guid organizationId, CancellationToken ct = default)
    {
        return await _db.EmailMessages
            .Where(m => m.OrganizationId == organizationId)
            .OrderByDescending(m => m.SentAtUtc)
            .ToListAsync(ct);
    }

    public async Task<List<EmailMessage>> GetUnassignedByOrganizationIdAsync(Guid organizationId, CancellationToken ct = default)
    {
        return await _db.EmailMessages
            .Where(m => m.OrganizationId == organizationId && m.ProjectId == null)
            .OrderBy(m => m.SentAtUtc)
            .ToListAsync(ct);
    }

    public async Task<List<EmailMessage>> GetUnassignedWithContactAsync(Guid organizationId, int limit = 100, CancellationToken ct = default)
    {
        return await _db.EmailMessages
            .Include(m => m.FromContact)
            .Where(m => m.OrganizationId == organizationId && m.ProjectId == null)
            .OrderByDescending(m => m.SentAtUtc)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<bool> ExistsByProviderMessageIdAsync(string providerMessageId, Guid organizationId, CancellationToken ct = default)
    {
        return await _db.EmailMessages
            .AnyAsync(m => m.ProviderMessageId == providerMessageId && m.OrganizationId == organizationId, ct);
    }

    public async Task AddAsync(EmailMessage message, CancellationToken ct = default)
    {
        _db.EmailMessages.Add(message);
        await _db.SaveChangesAsync(ct);
    }

    public async Task AddRangeAsync(IEnumerable<EmailMessage> messages, CancellationToken ct = default)
    {
        _db.EmailMessages.AddRange(messages);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(EmailMessage message, CancellationToken ct = default)
    {
        _db.EmailMessages.Update(message);
        await _db.SaveChangesAsync(ct);
    }
}
