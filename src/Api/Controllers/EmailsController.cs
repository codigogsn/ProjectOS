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
            aiReplyVariants = email.AiReplyVariants,
            aiReplyIntent = email.AiReplyIntent,
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

    [HttpPost("{emailId:guid}/reply")]
    public async Task<IActionResult> Reply(Guid emailId, [FromBody] ReplyRequest request, CancellationToken ct)
    {
        var email = await _emailRepo.GetByIdAsync(emailId, ct);
        if (email is null)
            return NotFound(new { error = "Email not found" });

        var action = (request.Action ?? "draft").ToLowerInvariant();
        var replyText = request.Body ?? "";

        _logger.LogInformation("Reply action={Action} for email {EmailId}, replyLength={Len}",
            action, emailId, replyText.Length);

        if (action == "send")
        {
            // TODO: Wire Gmail send API when ready
            // For now, log and return success to unblock the UI flow
            _logger.LogInformation("SEND requested for email {EmailId} — reply staged for send", emailId);

            return Ok(new
            {
                status = "staged",
                action = "send",
                message = "Reply staged for sending. Gmail send integration coming soon.",
                emailId,
                replyTo = email.FromAddress,
                threadId = email.ProviderThreadId
            });
        }

        // Draft: save reply text to AiSuggestedReply field
        email.AiSuggestedReply = replyText;
        await _emailRepo.UpdateAsync(email, ct);

        _logger.LogInformation("Draft saved for email {EmailId}", emailId);

        return Ok(new
        {
            status = "saved",
            action = "draft",
            message = "Draft saved successfully.",
            emailId
        });
    }

    [HttpPost("backfill-ai")]
    public async Task<IActionResult> BackfillAi(
        [FromQuery] Guid organizationId,
        [FromQuery] Guid? emailId = null,
        [FromQuery] int limit = 50,
        [FromQuery] bool force = false,
        CancellationToken ct = default)
    {
        if (organizationId == Guid.Empty)
            return BadRequest(new { error = "organizationId is required" });

        var guard = ValidateOrg(organizationId);
        if (guard is not null) return guard;

        var isSingle = emailId.HasValue && emailId.Value != Guid.Empty;
        var mode = force ? "force" : "only-null";
        var scope = isSingle ? "single" : "batch";

        _logger.LogInformation("AI backfill: mode={Mode}, scope={Scope}, org={OrgId}, emailId={EmailId}, limit={Limit}",
            mode, scope, organizationId, emailId, limit);

        List<EmailMessage> emails;

        if (isSingle)
        {
            var single = await _db.EmailMessages
                .FirstOrDefaultAsync(e => e.Id == emailId!.Value && e.OrganizationId == organizationId, ct);

            if (single is null)
                return NotFound(new { error = "Email not found in this organization" });

            emails = new List<EmailMessage> { single };
        }
        else
        {
            if (limit < 1) limit = 1;
            if (limit > 500) limit = 500;

            var query = _db.EmailMessages
                .Where(e => e.OrganizationId == organizationId);

            if (!force)
                query = query.Where(e => e.AiSummary == null || e.AiSummary == "Pending");

            emails = await query
                .OrderByDescending(e => e.CreatedAtUtc)
                .Take(limit)
                .ToListAsync(ct);
        }

        _logger.LogInformation("Backfill: {Count} emails to process (mode={Mode})", emails.Count, mode);

        var updated = 0;
        var failed = 0;
        var skipped = 0;
        var failures = new List<object>();

        foreach (var email in emails)
        {
            try
            {
                await _emailAi.ProcessEmailAsync(email, ct);
                await _db.SaveChangesAsync(ct);
                updated++;

                _logger.LogDebug("Backfill OK: {EmailId} → cat={Cat}, pri={Pri}",
                    email.Id, email.AiCategory, email.AiPriority);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Backfill failed for email {EmailId}: {Msg}", email.Id, ex.Message);
                failed++;
                failures.Add(new { emailId = email.Id, error = ex.Message });
                _db.Entry(email).State = EntityState.Unchanged;
            }
        }

        _logger.LogInformation("AI backfill complete: processed={Processed}, updated={Updated}, failed={Failed}, skipped={Skipped}, mode={Mode}",
            emails.Count, updated, failed, skipped, mode);

        return Ok(new
        {
            processed = emails.Count,
            updated,
            failed,
            skipped,
            mode,
            scope,
            failures
        });
    }
}

public record AssignEmailRequest(Guid ProjectId);
public record ReplyRequest(string? Body, string? Action);
