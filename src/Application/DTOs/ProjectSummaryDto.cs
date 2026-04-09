namespace ProjectOS.Application.DTOs;

public class ProjectSummaryDto
{
    public Guid Id { get; set; }
    public string SummaryText { get; set; } = string.Empty;
    public string CurrentStatus { get; set; } = string.Empty;
    public string PendingItems { get; set; } = string.Empty;
    public string SuggestedNextAction { get; set; } = string.Empty;
    public DateTime GeneratedAtUtc { get; set; }
}
