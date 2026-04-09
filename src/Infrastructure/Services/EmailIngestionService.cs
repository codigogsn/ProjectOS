using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectOS.Application.Interfaces;
using ProjectOS.Domain.Entities;
using ProjectOS.Infrastructure.Persistence;

namespace ProjectOS.Infrastructure.Services;

public partial class EmailIngestionService : IEmailIngestionService
{
    private readonly IGmailService _gmailService;
    private readonly IEmailMessageRepository _emailRepo;
    private readonly IContactRepository _contactRepo;
    private readonly EmailAiService _emailAi;
    private readonly AppDbContext _db;
    private readonly ILogger<EmailIngestionService> _logger;

    public EmailIngestionService(
        IGmailService gmailService,
        IEmailMessageRepository emailRepo,
        IContactRepository contactRepo,
        EmailAiService emailAi,
        AppDbContext db,
        ILogger<EmailIngestionService> logger)
    {
        _gmailService = gmailService;
        _emailRepo = emailRepo;
        _contactRepo = contactRepo;
        _emailAi = emailAi;
        _db = db;
        _logger = logger;
    }

    public async Task<EmailIngestionResult> IngestAsync(Guid organizationId, CancellationToken ct = default)
    {
        _logger.LogInformation("Starting Gmail fetch for org {OrgId}...", organizationId);

        // Ensure organization exists before ingesting
        await EnsureOrganizationExistsAsync(organizationId, ct);

        List<GmailMessageDto> emails;
        try
        {
            emails = await _gmailService.FetchRecentEmailsAsync(50, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gmail fetch failed — {ExType}: {ExMsg}", ex.GetType().Name, ex.Message);
            throw;
        }

        _logger.LogInformation("Fetched {Count} emails from Gmail for org {OrgId}", emails.Count, organizationId);

        var result = new EmailIngestionResult { Fetched = emails.Count };

        foreach (var dto in emails)
        {
            try
            {
                // Dedup check
                var exists = await _emailRepo.ExistsByProviderMessageIdAsync(dto.MessageId, organizationId, ct);
                if (exists)
                {
                    result.Duplicates++;
                    continue;
                }

                // Resolve sender contact
                var fromContact = await ResolveContactAsync(dto.From ?? "", organizationId, ct);

                // Resolve recipient contacts
                var toContactIdList = new List<Guid>();
                foreach (var toAddr in dto.To)
                {
                    try
                    {
                        var toContact = await ResolveContactAsync(toAddr, organizationId, ct);
                        toContactIdList.Add(toContact.Id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to resolve recipient contact for {Email}", toAddr);
                    }
                }

                // Truncate fields to fit DB constraints
                var subject = (dto.Subject ?? "(no subject)");
                if (subject.Length > 500) subject = subject[..500];

                var fromAddr = (dto.From ?? "unknown@unknown.com");
                if (fromAddr.Length > 256) fromAddr = fromAddr[..256];

                var toAddr2 = string.Join(", ", dto.To);
                if (toAddr2.Length > 2000) toAddr2 = toAddr2[..2000];

                // Build entity
                var message = new EmailMessage
                {
                    Subject = subject,
                    NormalizedSubject = NormalizeSubject(dto.Subject ?? ""),
                    Body = dto.BodyText ?? "",
                    FromAddress = fromAddr,
                    ToAddress = toAddr2,
                    SentAtUtc = dto.SentAtUtc,
                    ProviderMessageId = dto.MessageId,
                    ProviderThreadId = dto.ThreadId,
                    FromContactId = fromContact.Id,
                    ToContactIds = string.Join(",", toContactIdList),
                    OrganizationId = organizationId
                };

                await _emailRepo.AddAsync(message, ct);
                result.Saved++;

                // AI processing — non-blocking, failures logged and skipped
                try
                {
                    await _emailAi.ProcessEmailAsync(message, ct);
                    await _emailRepo.UpdateAsync(message, ct);
                    _logger.LogDebug("AI processed email {MessageId}", dto.MessageId);
                }
                catch (Exception aiEx)
                {
                    _logger.LogWarning(aiEx, "AI processing failed for email {MessageId} — continuing", dto.MessageId);
                }

                _logger.LogDebug("Saved email {MessageId} — subject: {Subject}", dto.MessageId, subject);
            }
            catch (DbUpdateException ex)
            {
                var inner = ex.InnerException?.Message ?? ex.Message;
                _logger.LogError(ex, "DB save failed for email {MessageId}: {Inner}", dto.MessageId, inner);
                result.Failed++;
                result.Failures.Add(new EmailFailure
                {
                    MessageId = dto.MessageId,
                    Stage = "db_save",
                    Message = $"DbUpdateException: {inner}"
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process email {MessageId}: {ExType} — {ExMsg}",
                    dto.MessageId, ex.GetType().Name, ex.Message);
                result.Failed++;
                result.Failures.Add(new EmailFailure
                {
                    MessageId = dto.MessageId,
                    Stage = "process_email",
                    Message = $"{ex.GetType().Name}: {ex.Message}"
                });
            }
        }

        _logger.LogInformation(
            "Email ingestion complete: {Fetched} fetched, {Saved} saved, {Duplicates} duplicates, {Failed} failed",
            result.Fetched, result.Saved, result.Duplicates, result.Failed);

        return result;
    }

    private async Task EnsureOrganizationExistsAsync(Guid organizationId, CancellationToken ct)
    {
        var exists = await _db.Organizations.AnyAsync(o => o.Id == organizationId, ct);
        if (!exists)
        {
            _logger.LogInformation("Organization {OrgId} does not exist — creating it", organizationId);
            _db.Organizations.Add(new Organization
            {
                Id = organizationId,
                Name = "Default Organization",
                IsActive = true
            });
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Organization {OrgId} created", organizationId);
        }
    }

    private async Task<Contact> ResolveContactAsync(string email, Guid organizationId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email))
            email = "unknown@unknown.com";

        if (email.Length > 256)
            email = email[..256];

        var contact = await _contactRepo.GetByEmailAsync(email, organizationId, ct);
        if (contact is not null)
            return contact;

        contact = new Contact
        {
            Email = email,
            FullName = ExtractNameFromEmail(email),
            OrganizationId = organizationId
        };

        await _contactRepo.AddAsync(contact, ct);
        _logger.LogInformation("Created new contact for {Email}", email);
        return contact;
    }

    private static string ExtractNameFromEmail(string email)
    {
        var local = email.Split('@')[0];
        var name = local.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ');
        return name.Length > 200 ? name[..200] : name;
    }

    internal static string NormalizeSubject(string subject)
    {
        var normalized = SubjectPrefixRegex().Replace(subject, "").Trim();
        return normalized.Length > 500 ? normalized[..500] : normalized;
    }

    [GeneratedRegex(@"^(?:(?:re|fwd?|fw)\s*:\s*)+", RegexOptions.IgnoreCase)]
    private static partial Regex SubjectPrefixRegex();
}
