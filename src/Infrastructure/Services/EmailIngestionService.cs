using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using ProjectOS.Application.Interfaces;
using ProjectOS.Domain.Entities;

namespace ProjectOS.Infrastructure.Services;

public partial class EmailIngestionService : IEmailIngestionService
{
    private readonly IGmailService _gmailService;
    private readonly IEmailMessageRepository _emailRepo;
    private readonly IContactRepository _contactRepo;
    private readonly ILogger<EmailIngestionService> _logger;

    public EmailIngestionService(
        IGmailService gmailService,
        IEmailMessageRepository emailRepo,
        IContactRepository contactRepo,
        ILogger<EmailIngestionService> logger)
    {
        _gmailService = gmailService;
        _emailRepo = emailRepo;
        _contactRepo = contactRepo;
        _logger = logger;
    }

    public async Task<EmailIngestionResult> IngestAsync(Guid organizationId, CancellationToken ct = default)
    {
        _logger.LogInformation("Starting Gmail fetch for org {OrgId}...", organizationId);

        List<GmailMessageDto> emails;
        try
        {
            emails = await _gmailService.FetchRecentEmailsAsync(50, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gmail fetch failed — {ExType}: {ExMsg}", ex.GetType().Name, ex.Message);
            throw; // Let controller handle with stage detection
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
                        _logger.LogWarning(ex, "Failed to resolve contact for {Email}", toAddr);
                    }
                }

                // Build entity
                var message = new EmailMessage
                {
                    Subject = dto.Subject ?? "(no subject)",
                    NormalizedSubject = NormalizeSubject(dto.Subject ?? ""),
                    Body = dto.BodyText ?? "",
                    FromAddress = dto.From ?? "",
                    ToAddress = string.Join(", ", dto.To),
                    SentAtUtc = dto.SentAtUtc,
                    ProviderMessageId = dto.MessageId,
                    ProviderThreadId = dto.ThreadId,
                    FromContactId = fromContact.Id,
                    ToContactIds = string.Join(",", toContactIdList),
                    OrganizationId = organizationId
                };

                await _emailRepo.AddAsync(message, ct);
                result.Saved++;

                _logger.LogDebug("Saved email {MessageId} — subject: {Subject}", dto.MessageId, dto.Subject);
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

    private async Task<Contact> ResolveContactAsync(string email, Guid organizationId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email))
            email = "unknown@unknown.com";

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
        return local.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ');
    }

    internal static string NormalizeSubject(string subject)
    {
        return SubjectPrefixRegex().Replace(subject, "").Trim();
    }

    [GeneratedRegex(@"^(?:(?:re|fwd?|fw)\s*:\s*)+", RegexOptions.IgnoreCase)]
    private static partial Regex SubjectPrefixRegex();
}
