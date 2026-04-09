namespace ProjectOS.Domain.Entities;

public class ActionItem : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = "Pending";
    public int Priority { get; set; }

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
}
