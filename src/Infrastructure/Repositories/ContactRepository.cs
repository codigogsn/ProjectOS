using Microsoft.EntityFrameworkCore;
using ProjectOS.Application.Interfaces;
using ProjectOS.Domain.Entities;
using ProjectOS.Infrastructure.Persistence;

namespace ProjectOS.Infrastructure.Repositories;

public class ContactRepository : IContactRepository
{
    private readonly AppDbContext _db;

    public ContactRepository(AppDbContext db) => _db = db;

    public async Task<Contact?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.Contacts.FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task<Contact?> GetByEmailAsync(string email, Guid organizationId, CancellationToken ct = default)
    {
        return await _db.Contacts
            .FirstOrDefaultAsync(c => c.Email == email && c.OrganizationId == organizationId, ct);
    }

    public async Task<List<Contact>> GetByOrganizationIdAsync(Guid organizationId, CancellationToken ct = default)
    {
        return await _db.Contacts
            .Where(c => c.OrganizationId == organizationId)
            .OrderBy(c => c.FullName)
            .ToListAsync(ct);
    }

    public async Task AddAsync(Contact contact, CancellationToken ct = default)
    {
        _db.Contacts.Add(contact);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Contact contact, CancellationToken ct = default)
    {
        _db.Contacts.Update(contact);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var contact = await _db.Contacts.FindAsync(new object[] { id }, ct);
        if (contact is not null)
        {
            _db.Contacts.Remove(contact);
            await _db.SaveChangesAsync(ct);
        }
    }
}
