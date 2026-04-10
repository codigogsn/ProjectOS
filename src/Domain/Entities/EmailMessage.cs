namespace ProjectOS.Domain.Entities;

public class EmailMessage : BaseEntity
{
    public string Subject { get; set; } = string.Empty;
    public string NormalizedSubject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string ToAddress { get; set; } = string.Empty;
    public DateTime SentAtUtc { get; set; }
    public bool IsRead { get; set; }

    public string? ProviderMessageId { get; set; }
    public string? ProviderThreadId { get; set; }

    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }

    public Guid? FromContactId { get; set; }
    public Contact? FromContact { get; set; }

    public string? ToContactIds { get; set; }

    public decimal? AssignmentConfidence { get; set; }
    public string? AssignmentSource { get; set; }

    public string? AiSummary { get; set; }
    public string? AiSuggestedReply { get; set; }
    public string? AiReplyVariants { get; set; }
    public string? AiReplyIntent { get; set; }
    public string? AiCategory { get; set; }
    public string? AiPriority { get; set; }

    public Guid OrganizationId { get; set; }
}
