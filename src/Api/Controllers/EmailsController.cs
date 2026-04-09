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
public class EmailsController : ControllerBase
{
    private readonly IEmailIngestionService _ingestionService;
    private readonly IEmailMessageRepository _emailRepo;
    private readonly IProjectRepository _projectRepo;
    private readonly ILogger<EmailsController> _logger;

    public EmailsController(
        IEmailIngestionService ingestionService,
        IEmailMessageRepository emailRepo,
        IProjectRepository projectRepo,
        ILogger<EmailsController> logger)
    {
        _ingestionService = ingestionService;
        _emailRepo = emailRepo;
        _projectRepo = projectRepo;
        _logger = logger;
    }

    [HttpPost("sync")]
    public async Task<IActionResult> Sync([FromQuery] Guid organizationId, CancellationToken ct)
    {
        _logger.LogInformation("Email sync triggered for organization {OrgId}", organizationId);

        var result = await _ingestionService.IngestAsync(organizationId, ct);

        return Ok(new
        {
            result.Fetched,
            result.Saved,
            result.Duplicates,
            Message = $"Sync complete: {result.Saved} new emails saved, {result.Duplicates} duplicates skipped"
        });
    }

    [HttpGet("unassigned")]
    public async Task<IActionResult> GetUnassigned([FromQuery] Guid organizationId, CancellationToken ct)
    {
        var emails = await _emailRepo.GetUnassignedWithContactAsync(organizationId, 100, ct);

        _logger.LogInformation("Returning {Count} unassigned emails for org {OrgId}", emails.Count, organizationId);

        var result = emails.Select(e => new EmailDto
        {
            Id = e.Id,
            Subject = e.Subject,
            BodyPreview = e.Body.Length > 300 ? e.Body[..300] + "..." : e.Body,
            FromName = e.FromContact?.FullName ?? "",
            FromEmail = e.FromAddress,
            ToAddress = e.ToAddress,
            SentAtUtc = e.SentAtUtc
        });

        return Ok(result);
    }

    [HttpPost("{emailId:guid}/assign")]
    public async Task<IActionResult> Assign(Guid emailId, [FromBody] AssignEmailRequest request, CancellationToken ct)
    {
        var email = await _emailRepo.GetByIdAsync(emailId, ct);
        if (email is null)
            return NotFound(new { message = "Email not found" });

        var project = await _projectRepo.GetByIdAsync(request.ProjectId, ct);
        if (project is null)
            return NotFound(new { message = "Project not found" });

        email.ProjectId = request.ProjectId;
        email.AssignmentSource = "manual";
        email.AssignmentConfidence = 1.0m;
        await _emailRepo.UpdateAsync(email, ct);

        project.EmailCount++;
        if (email.SentAtUtc > (project.LastActivityAtUtc ?? DateTime.MinValue))
            project.LastActivityAtUtc = email.SentAtUtc;
        await _projectRepo.UpdateAsync(project, ct);

        _logger.LogInformation("Email {EmailId} manually assigned to project {ProjectId}", emailId, request.ProjectId);

        return Ok(new { message = "Email assigned", projectId = request.ProjectId });
    }

    [HttpPost("{emailId:guid}/create-project")]
    public async Task<IActionResult> CreateProjectFromEmail(Guid emailId, CancellationToken ct)
    {
        var email = await _emailRepo.GetByIdAsync(emailId, ct);
        if (email is null)
            return NotFound(new { message = "Email not found" });

        var projectName = !string.IsNullOrWhiteSpace(email.NormalizedSubject)
            ? email.NormalizedSubject
            : $"Project from email {email.SentAtUtc:yyyy-MM-dd}";

        if (projectName.Length > 200)
            projectName = projectName[..200];

        var project = new Project
        {
            Name = projectName,
            Status = ProjectStatus.Active,
            OrganizationId = email.OrganizationId,
            LastActivityAtUtc = email.SentAtUtc,
            StartDate = email.SentAtUtc,
            EmailCount = 1
        };

        await _projectRepo.AddAsync(project, ct);

        email.ProjectId = project.Id;
        email.AssignmentSource = "manual";
        email.AssignmentConfidence = 1.0m;
        await _emailRepo.UpdateAsync(email, ct);

        _logger.LogInformation("Created project {ProjectId} from email {EmailId}", project.Id, emailId);

        return Ok(new { message = "Project created", projectId = project.Id, projectName = project.Name });
    }
}

public record AssignEmailRequest(Guid ProjectId);
