namespace ProjectOS.Application.DTOs;

public class EmailDto
{
    public Guid Id { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string BodyPreview { get; set; } = string.Empty;
    public string FromName { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
    public string ToAddress { get; set; } = string.Empty;
    public DateTime SentAtUtc { get; set; }
    public decimal? AssignmentConfidence { get; set; }
    public string? AssignmentSource { get; set; }
}
