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
        var emails = await _gmailService.FetchRecentEmailsAsync(50, ct);
        _logger.LogInformation("Fetched {Count} emails from Gmail", emails.Count);

        var saved = 0;
        var duplicates = 0;

        foreach (var dto in emails)
        {
            var exists = await _emailRepo.ExistsByProviderMessageIdAsync(dto.MessageId, organizationId, ct);
            if (exists)
            {
                duplicates++;
                continue;
            }

            var fromContact = await ResolveContactAsync(dto.From, organizationId, ct);

            var toContactIdList = new List<Guid>();
            foreach (var toAddr in dto.To)
            {
                var toContact = await ResolveContactAsync(toAddr, organizationId, ct);
                toContactIdList.Add(toContact.Id);
            }

            var message = new EmailMessage
            {
                Subject = dto.Subject,
                NormalizedSubject = NormalizeSubject(dto.Subject),
                Body = dto.BodyText,
                FromAddress = dto.From,
                ToAddress = string.Join(", ", dto.To),
                SentAtUtc = dto.SentAtUtc,
                ProviderMessageId = dto.MessageId,
                ProviderThreadId = dto.ThreadId,
                FromContactId = fromContact.Id,
                ToContactIds = string.Join(",", toContactIdList),
                OrganizationId = organizationId
            };

            await _emailRepo.AddAsync(message, ct);
            saved++;
        }

        _logger.LogInformation(
            "Email ingestion complete: {Fetched} fetched, {Saved} saved, {Duplicates} duplicates skipped",
            emails.Count, saved, duplicates);

        return new EmailIngestionResult
        {
            Fetched = emails.Count,
            Saved = saved,
            Duplicates = duplicates
        };
    }

    private async Task<Contact> ResolveContactAsync(string email, Guid organizationId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            email = "unknown@unknown.com";
        }

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
