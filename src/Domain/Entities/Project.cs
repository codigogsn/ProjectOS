using ProjectOS.Domain.Enums;

namespace ProjectOS.Domain.Entities;

public class Project : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ProjectStatus Status { get; set; } = ProjectStatus.Draft;
    public DateTime? StartDate { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    public ICollection<ActionItem> ActionItems { get; set; } = new List<ActionItem>();
    public ICollection<ProjectSummary> Summaries { get; set; } = new List<ProjectSummary>();
    public ICollection<EmailMessage> EmailMessages { get; set; } = new List<EmailMessage>();
}
