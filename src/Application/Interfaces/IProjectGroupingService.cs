namespace ProjectOS.Application.Interfaces;

public interface IProjectGroupingService
{
    Task<GroupingResult> GroupEmailsAsync(Guid organizationId, CancellationToken ct = default);
}

public class GroupingResult
{
    public int EmailsProcessed { get; set; }
    public int AssignedToExisting { get; set; }
    public int NewProjectsCreated { get; set; }
}
