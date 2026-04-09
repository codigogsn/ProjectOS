namespace ProjectOS.Application.DTOs;

public class ProjectListItemDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int EmailCount { get; set; }
    public DateTime? LastActivityAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
