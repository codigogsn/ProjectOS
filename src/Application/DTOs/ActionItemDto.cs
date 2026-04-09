namespace ProjectOS.Application.DTOs;

public class ActionItemDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int Priority { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
