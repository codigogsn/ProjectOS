using ProjectOS.Domain.Entities;

namespace ProjectOS.Application.Interfaces;

public interface IEmailMessageRepository
{
    Task<EmailMessage?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<EmailMessage>> GetByProjectIdAsync(Guid projectId, CancellationToken ct = default);
    Task<List<EmailMessage>> GetRecentByProjectIdAsync(Guid projectId, int limit = 50, CancellationToken ct = default);
    Task<List<EmailMessage>> GetByOrganizationIdAsync(Guid organizationId, CancellationToken ct = default);
    Task<List<EmailMessage>> GetUnassignedByOrganizationIdAsync(Guid organizationId, CancellationToken ct = default);
    Task<List<EmailMessage>> GetUnassignedWithContactAsync(Guid organizationId, int limit = 100, CancellationToken ct = default);
    Task<bool> ExistsByProviderMessageIdAsync(string providerMessageId, Guid organizationId, CancellationToken ct = default);
    Task AddAsync(EmailMessage message, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<EmailMessage> messages, CancellationToken ct = default);
    Task UpdateAsync(EmailMessage message, CancellationToken ct = default);
}
