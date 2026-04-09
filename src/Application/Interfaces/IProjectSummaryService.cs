using ProjectOS.Domain.Entities;

namespace ProjectOS.Application.Interfaces;

public interface IProjectSummaryService
{
    Task<ProjectSummary> GenerateSummaryAsync(Guid projectId, CancellationToken ct = default);
    Task<ProjectSummary?> GetLatestSummaryAsync(Guid projectId, CancellationToken ct = default);
}
