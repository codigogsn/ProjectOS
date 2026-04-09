using Microsoft.Extensions.Logging;
using ProjectOS.Application.Interfaces;
using ProjectOS.Domain.Entities;
using ProjectOS.Domain.Enums;

namespace ProjectOS.Infrastructure.Services;

public class ProjectGroupingService : IProjectGroupingService
{
    private readonly IEmailMessageRepository _emailRepo;
    private readonly IProjectRepository _projectRepo;
    private readonly ILogger<ProjectGroupingService> _logger;

    public ProjectGroupingService(
        IEmailMessageRepository emailRepo,
        IProjectRepository projectRepo,
        ILogger<ProjectGroupingService> logger)
    {
        _emailRepo = emailRepo;
        _projectRepo = projectRepo;
        _logger = logger;
    }

    public async Task<GroupingResult> GroupEmailsAsync(Guid organizationId, CancellationToken ct = default)
    {
        var unassigned = await _emailRepo.GetUnassignedByOrganizationIdAsync(organizationId, ct);
        _logger.LogInformation("Found {Count} unassigned emails for org {OrgId}", unassigned.Count, organizationId);

        if (unassigned.Count == 0)
            return new GroupingResult();

        var result = new GroupingResult { EmailsProcessed = unassigned.Count };
        var threadAssignments = 0;
        var subjectAssignments = 0;
        var newProjects = 0;

        // Cache: normalized subject -> newly created project in this run,
        // so multiple emails with the same subject batch into one new project.
        var newProjectCache = new Dictionary<string, Project>(StringComparer.OrdinalIgnoreCase);

        foreach (var email in unassigned)
        {
            // Rule 1: Thread match
            if (!string.IsNullOrEmpty(email.ProviderThreadId))
            {
                var project = await _projectRepo.FindByThreadIdAsync(email.ProviderThreadId, organizationId, ct);
                if (project is not null)
                {
                    await AssignEmailToProject(email, project, 0.95m, "thread", ct);
                    threadAssignments++;
                    result.AssignedToExisting++;
                    continue;
                }
            }

            // Rule 2: Normalized subject + sender overlap
            if (!string.IsNullOrWhiteSpace(email.NormalizedSubject))
            {
                var candidates = await _projectRepo.FindByNormalizedSubjectAsync(email.NormalizedSubject, organizationId, ct);

                if (candidates.Count > 0 && email.FromContactId.HasValue)
                {
                    // Pick the project whose existing emails share a contact with this email's sender
                    var matched = await FindProjectWithContactOverlap(candidates, email.FromContactId.Value, ct);
                    if (matched is not null)
                    {
                        await AssignEmailToProject(email, matched, 0.75m, "subject_contact", ct);
                        subjectAssignments++;
                        result.AssignedToExisting++;
                        continue;
                    }
                }

                // Subject matches but no contact overlap — still assign to first candidate
                // with lower confidence
                if (candidates.Count > 0)
                {
                    await AssignEmailToProject(email, candidates[0], 0.50m, "subject_contact", ct);
                    subjectAssignments++;
                    result.AssignedToExisting++;
                    continue;
                }
            }

            // Rule 3: Create new project
            var subjectKey = string.IsNullOrWhiteSpace(email.NormalizedSubject) ? "" : email.NormalizedSubject;

            // Check if we already created a project for this subject in this batch
            if (!string.IsNullOrEmpty(subjectKey) && newProjectCache.TryGetValue(subjectKey, out var cached))
            {
                await AssignEmailToProject(email, cached, 0.90m, "auto_new_project", ct);
                result.AssignedToExisting++;
                continue;
            }

            var newProject = await CreateProjectFromEmail(email, organizationId, ct);

            if (!string.IsNullOrEmpty(subjectKey))
                newProjectCache[subjectKey] = newProject;

            await AssignEmailToProject(email, newProject, 1.0m, "auto_new_project", ct);
            newProjects++;
            result.NewProjectsCreated++;
        }

        _logger.LogInformation(
            "Grouping complete: {Processed} processed, {Thread} by thread, {Subject} by subject/contact, {New} new projects",
            result.EmailsProcessed, threadAssignments, subjectAssignments, newProjects);

        return result;
    }

    private async Task<Project?> FindProjectWithContactOverlap(List<Project> candidates, Guid fromContactId, CancellationToken ct)
    {
        foreach (var project in candidates)
        {
            var projectEmails = await _emailRepo.GetByProjectIdAsync(project.Id, ct);
            var hasOverlap = projectEmails.Any(e =>
                e.FromContactId == fromContactId ||
                (e.ToContactIds is not null && e.ToContactIds.Contains(fromContactId.ToString())));

            if (hasOverlap)
                return project;
        }

        return null;
    }

    private async Task AssignEmailToProject(EmailMessage email, Project project, decimal confidence, string source, CancellationToken ct)
    {
        email.ProjectId = project.Id;
        email.AssignmentConfidence = confidence;
        email.AssignmentSource = source;
        await _emailRepo.UpdateAsync(email, ct);

        project.EmailCount++;
        if (email.SentAtUtc > (project.LastActivityAtUtc ?? DateTime.MinValue))
            project.LastActivityAtUtc = email.SentAtUtc;

        await _projectRepo.UpdateAsync(project, ct);
    }

    private async Task<Project> CreateProjectFromEmail(EmailMessage email, Guid organizationId, CancellationToken ct)
    {
        var name = !string.IsNullOrWhiteSpace(email.NormalizedSubject)
            ? Truncate(email.NormalizedSubject, 200)
            : $"Project {DateTime.UtcNow:yyyy-MM-dd HH:mm}";

        var project = new Project
        {
            Name = name,
            Status = ProjectStatus.Active,
            OrganizationId = organizationId,
            LastActivityAtUtc = email.SentAtUtc,
            StartDate = email.SentAtUtc
        };

        await _projectRepo.AddAsync(project, ct);
        _logger.LogInformation("Created project '{Name}' ({Id}) for org {OrgId}", project.Name, project.Id, organizationId);
        return project;
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
