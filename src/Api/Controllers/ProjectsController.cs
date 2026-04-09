using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProjectOS.Application.Interfaces;
using ProjectOS.Domain.Entities;
using ProjectOS.Domain.Enums;

namespace ProjectOS.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProjectsController : ControllerBase
{
    private readonly IProjectRepository _projectRepo;
    private readonly IProjectGroupingService _groupingService;
    private readonly ILogger<ProjectsController> _logger;

    public ProjectsController(
        IProjectRepository projectRepo,
        IProjectGroupingService groupingService,
        ILogger<ProjectsController> logger)
    {
        _projectRepo = projectRepo;
        _groupingService = groupingService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] Guid organizationId, CancellationToken ct)
    {
        var projects = await _projectRepo.GetByOrganizationIdAsync(organizationId, ct);
        return Ok(projects.Select(p => new
        {
            p.Id,
            p.Name,
            p.Description,
            Status = p.Status.ToString(),
            p.StartDate,
            p.DueDate,
            p.EmailCount,
            p.LastActivityAtUtc,
            p.CreatedAtUtc
        }));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var project = await _projectRepo.GetByIdAsync(id, ct);
        if (project is null)
            return NotFound();

        return Ok(new
        {
            project.Id,
            project.Name,
            project.Description,
            Status = project.Status.ToString(),
            project.StartDate,
            project.DueDate,
            project.CompletedAtUtc,
            project.CreatedAtUtc,
            project.UpdatedAtUtc,
            ActionItems = project.ActionItems.Select(a => new
            {
                a.Id,
                a.Title,
                a.IsCompleted,
                a.Priority,
                a.DueDate
            }),
            Summaries = project.Summaries.Select(s => new
            {
                s.Id,
                s.Title,
                s.GeneratedAtUtc
            })
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateProjectRequest request, CancellationToken ct)
    {
        var project = new Project
        {
            Name = request.Name,
            Description = request.Description,
            OrganizationId = request.OrganizationId,
            Status = ProjectStatus.Draft,
            StartDate = request.StartDate,
            DueDate = request.DueDate
        };

        await _projectRepo.AddAsync(project, ct);
        _logger.LogInformation("Project {ProjectId} created for org {OrgId}", project.Id, project.OrganizationId);

        return CreatedAtAction(nameof(GetById), new { id = project.Id }, new { project.Id, project.Name });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateProjectRequest request, CancellationToken ct)
    {
        var project = await _projectRepo.GetByIdAsync(id, ct);
        if (project is null)
            return NotFound();

        project.Name = request.Name ?? project.Name;
        project.Description = request.Description ?? project.Description;

        if (request.Status is not null && Enum.TryParse<ProjectStatus>(request.Status, out var status))
        {
            project.Status = status;
            if (status == ProjectStatus.Completed)
                project.CompletedAtUtc = DateTime.UtcNow;
        }

        project.DueDate = request.DueDate ?? project.DueDate;

        await _projectRepo.UpdateAsync(project, ct);
        return NoContent();
    }

    [HttpPost("group")]
    public async Task<IActionResult> Group([FromQuery] Guid organizationId, CancellationToken ct)
    {
        _logger.LogInformation("Project grouping triggered for organization {OrgId}", organizationId);

        var result = await _groupingService.GroupEmailsAsync(organizationId, ct);

        return Ok(new
        {
            result.EmailsProcessed,
            result.AssignedToExisting,
            result.NewProjectsCreated,
            Message = $"Grouping complete: {result.AssignedToExisting} assigned to existing, {result.NewProjectsCreated} new projects created"
        });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _projectRepo.DeleteAsync(id, ct);
        return NoContent();
    }
}

public record CreateProjectRequest(string Name, string? Description, Guid OrganizationId, DateTime? StartDate, DateTime? DueDate);
public record UpdateProjectRequest(string? Name, string? Description, string? Status, DateTime? DueDate);
