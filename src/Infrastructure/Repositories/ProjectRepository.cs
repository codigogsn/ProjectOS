using Microsoft.EntityFrameworkCore;
using ProjectOS.Application.Interfaces;
using ProjectOS.Domain.Entities;
using ProjectOS.Infrastructure.Persistence;

namespace ProjectOS.Infrastructure.Repositories;

public class ProjectRepository : IProjectRepository
{
    private readonly AppDbContext _db;

    public ProjectRepository(AppDbContext db) => _db = db;

    public async Task<Project?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.Projects
            .Include(p => p.ActionItems)
            .Include(p => p.Summaries)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<List<Project>> GetByOrganizationIdAsync(Guid organizationId, CancellationToken ct = default)
    {
        return await _db.Projects
            .Where(p => p.OrganizationId == organizationId)
            .OrderByDescending(p => p.LastActivityAtUtc ?? p.CreatedAtUtc)
            .ToListAsync(ct);
    }

    public async Task<Project?> FindByThreadIdAsync(string providerThreadId, Guid organizationId, CancellationToken ct = default)
    {
        return await _db.EmailMessages
            .Where(m => m.ProviderThreadId == providerThreadId
                        && m.OrganizationId == organizationId
                        && m.ProjectId != null)
            .Select(m => m.Project!)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<List<Project>> FindByNormalizedSubjectAsync(string normalizedSubject, Guid organizationId, CancellationToken ct = default)
    {
        return await _db.EmailMessages
            .Where(m => m.NormalizedSubject == normalizedSubject
                        && m.OrganizationId == organizationId
                        && m.ProjectId != null)
            .Select(m => m.Project!)
            .Distinct()
            .ToListAsync(ct);
    }

    public async Task AddAsync(Project project, CancellationToken ct = default)
    {
        _db.Projects.Add(project);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Project project, CancellationToken ct = default)
    {
        _db.Projects.Update(project);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var project = await _db.Projects.FindAsync(new object[] { id }, ct);
        if (project is not null)
        {
            _db.Projects.Remove(project);
            await _db.SaveChangesAsync(ct);
        }
    }
}
