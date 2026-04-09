using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectOS.Application.DTOs;
using ProjectOS.Application.Interfaces;
using ProjectOS.Domain.Entities;
using ProjectOS.Domain.Enums;
using ProjectOS.Infrastructure.Persistence;
using ProjectOS.Infrastructure.Services;

namespace ProjectOS.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[AllowAnonymous] // MVP: no login UI yet — will add [Authorize] when auth is implemented
public class EmailsController : ControllerBase
{
    private readonly IEmailIngestionService _ingestionService;
    private readonly IEmailMessageRepository _emailRepo;
    private readonly IProjectRepository _projectRepo;
    private readonly EmailAiService _emailAi;
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<EmailsController> _logger;

    public EmailsController(
        IEmailIngestionService ingestionService,
        IEmailMessageRepository emailRepo,
        IProjectRepository projectRepo,
        EmailAiService emailAi,
        AppDbContext db,
        IConfiguration config,
        ILogger<EmailsController> logger)
    {
        _ingestionService = ingestionService;
        _emailRepo = emailRepo;
        _projectRepo = projectRepo;
        _emailAi = emailAi;
        _db = db;
        _config = config;
        _logger = logger;
    }

    private IActionResult? ValidateOrg(Guid organizationId)
    {
        var allowed = _config["DefaultOrganizationId"];
        if (!string.IsNullOrEmpty(allowed) && !organizationId.ToString().Equals(allowed, StringComparison.OrdinalIgnoreCase))
            return Forbid();
        return null;
    }

    [HttpPost("sync")]
    public async Task<IActionResult> Sync([FromQuery] Guid organizationId, CancellationToken ct)
    {
        var guard = ValidateOrg(organizationId);
        if (guard is not null) return guard;

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

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] Guid organizationId, CancellationToken ct)
    {
        if (organizationId == Guid.Empty)
            return BadRequest(new { error = "organizationId is required" });

        var guard = ValidateOrg(organizationId);
        if (guard is not null) return guard;

        var emails = await _emailRepo.GetByOrganizationIdAsync(organizationId, ct);

        _logger.LogInformation("Returning {Count} emails for org {OrgId}", emails.Count, organizationId);

        var result = emails.Take(200).Select(e => new
        {
            e.Id,
            fromEmail = e.FromAddress,
            e.Subject,
            bodyPreview = e.Body.Length > 200 ? e.Body[..200] + "..." : e.Body,
            e.NormalizedSubject,
            e.ProviderMessageId,
            e.SentAtUtc,
            e.CreatedAtUtc,
            projectId = e.ProjectId,
            assignmentSource = e.AssignmentSource,
            aiSummary = e.AiSummary,
            aiCategory = e.AiCategory,
            aiPriority = e.AiPriority
        });

        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var email = await _emailRepo.GetByIdAsync(id, ct);
        if (email is null)
            return NotFound(new { error = "Email not found" });

        _logger.LogInformation("Returning email detail {EmailId}", id);

        return Ok(new
        {
            email.Id,
            organizationId = email.OrganizationId,
            fromEmail = email.FromAddress,
            toAddress = email.ToAddress,
            email.Subject,
            email.NormalizedSubject,
            email.Body,
            email.SentAtUtc,
            email.CreatedAtUtc,
            gmailMessageId = email.ProviderMessageId,
            threadId = email.ProviderThreadId,
            projectId = email.ProjectId,
            fromContactId = email.FromContactId,
            assignmentSource = email.AssignmentSource,
            assignmentConfidence = email.AssignmentConfidence,
            aiSummary = email.AiSummary,
            aiSuggestedReply = email.AiSuggestedReply,
            aiCategory = email.AiCategory,
            aiPriority = email.AiPriority
        });
    }

    [HttpGet("unassigned")]
    public async Task<IActionResult> GetUnassigned([FromQuery] Guid organizationId, CancellationToken ct)
    {
        var guard = ValidateOrg(organizationId);
        if (guard is not null) return guard;

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

        // Decrement old project's count if reassigning
        if (email.ProjectId.HasValue && email.ProjectId.Value != request.ProjectId)
        {
            var oldProject = await _projectRepo.GetByIdAsync(email.ProjectId.Value, ct);
            if (oldProject is not null && oldProject.EmailCount > 0)
            {
                oldProject.EmailCount--;
                await _projectRepo.UpdateAsync(oldProject, ct);
            }
        }

        var wasUnassigned = !email.ProjectId.HasValue;

        email.ProjectId = request.ProjectId;
        email.AssignmentSource = "manual";
        email.AssignmentConfidence = 1.0m;
        await _emailRepo.UpdateAsync(email, ct);

        // Only increment if this is a new assignment or reassignment to a different project
        if (wasUnassigned || email.ProjectId != request.ProjectId)
        {
            project.EmailCount++;
        }
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

        // Decrement old project's count if email was previously assigned
        if (email.ProjectId.HasValue)
        {
            var oldProject = await _projectRepo.GetByIdAsync(email.ProjectId.Value, ct);
            if (oldProject is not null && oldProject.EmailCount > 0)
            {
                oldProject.EmailCount--;
                await _projectRepo.UpdateAsync(oldProject, ct);
            }
        }

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

    [HttpPost("backfill-ai")]
    public async Task<IActionResult> BackfillAi(
        [FromQuery] Guid organizationId,
        [FromQuery] int limit = 50,
        [FromQuery] bool onlyNull = true,
        CancellationToken ct = default)
    {
        if (organizationId == Guid.Empty)
            return BadRequest(new { error = "organizationId is required" });

        var guard = ValidateOrg(organizationId);
        if (guard is not null) return guard;

        if (limit < 1) limit = 1;
        if (limit > 200) limit = 200;

        _logger.LogInformation("AI backfill started for org {OrgId}, limit={Limit}, onlyNull={OnlyNull}",
            organizationId, limit, onlyNull);

        var query = _db.EmailMessages
            .Where(e => e.OrganizationId == organizationId);

        if (onlyNull)
            query = query.Where(e => e.AiSummary == null || e.AiSummary == "Pending");

        var emails = await query
            .OrderByDescending(e => e.CreatedAtUtc)
            .Take(limit)
            .ToListAsync(ct);

        _logger.LogInformation("Found {Count} emails to backfill", emails.Count);

        var updated = 0;
        var failed = 0;
        var failures = new List<object>();

        foreach (var email in emails)
        {
            try
            {
                _logger.LogDebug("Backfilling AI for email {EmailId}", email.Id);

                await _emailAi.ProcessEmailAsync(email, ct);
                await _db.SaveChangesAsync(ct);

                updated++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI backfill failed for email {EmailId}", email.Id);
                failed++;
                failures.Add(new { emailId = email.Id, error = ex.Message });

                // Detach the failed entity to prevent it from blocking subsequent saves
                _db.Entry(email).State = EntityState.Unchanged;
            }
        }

        _logger.LogInformation("AI backfill complete: {Processed} processed, {Updated} updated, {Failed} failed",
            emails.Count, updated, failed);

        return Ok(new
        {
            processed = emails.Count,
            updated,
            failed,
            failures
        });
    }
}

public record AssignEmailRequest(Guid ProjectId);
