using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProjectOS.Application.DTOs;
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
    private readonly IEmailMessageRepository _emailRepo;
    private readonly IProjectGroupingService _groupingService;
    private readonly ILogger<ProjectsController> _logger;

    public ProjectsController(
        IProjectRepository projectRepo,
        IEmailMessageRepository emailRepo,
        IProjectGroupingService groupingService,
        ILogger<ProjectsController> logger)
    {
        _projectRepo = projectRepo;
        _emailRepo = emailRepo;
        _groupingService = groupingService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] Guid organizationId, CancellationToken ct)
    {
        var projects = await _projectRepo.GetByOrganizationIdAsync(organizationId, ct);

        _logger.LogInformation("Returning {Count} projects for org {OrgId}", projects.Count, organizationId);

        var result = projects.Select(p => new ProjectListItemDto
        {
            Id = p.Id,
            Name = p.Name,
            Status = p.Status.ToString(),
            EmailCount = p.EmailCount,
            LastActivityAtUtc = p.LastActivityAtUtc,
            CreatedAtUtc = p.CreatedAtUtc
        });

        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var project = await _projectRepo.GetByIdAsync(id, ct);
        if (project is null)
            return NotFound();

        var emails = await _emailRepo.GetRecentByProjectIdAsync(id, 50, ct);

        _logger.LogInformation("Returning project {ProjectId} with {EmailCount} emails", id, emails.Count);

        var dto = new ProjectDetailDto
        {
            Id = project.Id,
            Name = project.Name,
            Description = project.Description,
            Status = project.Status.ToString(),
            EmailCount = project.EmailCount,
            StartDate = project.StartDate,
            DueDate = project.DueDate,
            LastActivityAtUtc = project.LastActivityAtUtc,
            CompletedAtUtc = project.CompletedAtUtc,
            CreatedAtUtc = project.CreatedAtUtc,
            Emails = emails.Select(e => new EmailDto
            {
                Id = e.Id,
                Subject = e.Subject,
                BodyPreview = TrimBody(e.Body, 500),
                FromName = e.FromContact?.FullName ?? "",
                FromEmail = e.FromAddress,
                ToAddress = e.ToAddress,
                SentAtUtc = e.SentAtUtc,
                AssignmentConfidence = e.AssignmentConfidence,
                AssignmentSource = e.AssignmentSource
            }).ToList()
        };

        return Ok(dto);
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

    private static string TrimBody(string body, int maxLength)
    {
        if (string.IsNullOrEmpty(body) || body.Length <= maxLength)
            return body;
        return body[..maxLength] + "...";
    }
}

public record CreateProjectRequest(string Name, string? Description, Guid OrganizationId, DateTime? StartDate, DateTime? DueDate);
public record UpdateProjectRequest(string? Name, string? Description, string? Status, DateTime? DueDate);
