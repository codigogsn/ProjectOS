using ProjectOS.Domain.Entities;

namespace ProjectOS.Application.Interfaces;

public interface IContactRepository
{
    Task<Contact?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Contact?> GetByEmailAsync(string email, Guid organizationId, CancellationToken ct = default);
    Task<List<Contact>> GetByOrganizationIdAsync(Guid organizationId, CancellationToken ct = default);
    Task AddAsync(Contact contact, CancellationToken ct = default);
    Task UpdateAsync(Contact contact, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
