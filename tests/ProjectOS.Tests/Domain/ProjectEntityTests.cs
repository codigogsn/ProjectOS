using ProjectOS.Domain.Entities;
using ProjectOS.Domain.Enums;
using Xunit;

namespace ProjectOS.Tests.Domain;

public class ProjectEntityTests
{
    [Fact]
    public void NewProject_HasDefaultValues()
    {
        var project = new Project();

        Assert.NotEqual(Guid.Empty, project.Id);
        Assert.Equal(ProjectStatus.Draft, project.Status);
        Assert.True(project.CreatedAtUtc <= DateTime.UtcNow);
        Assert.Null(project.CompletedAtUtc);
    }

    [Fact]
    public void Project_CanSetProperties()
    {
        var orgId = Guid.NewGuid();
        var project = new Project
        {
            Name = "Test Project",
            Description = "A test project",
            OrganizationId = orgId,
            Status = ProjectStatus.Active,
            StartDate = DateTime.UtcNow,
            DueDate = DateTime.UtcNow.AddDays(30)
        };

        Assert.Equal("Test Project", project.Name);
        Assert.Equal(orgId, project.OrganizationId);
        Assert.Equal(ProjectStatus.Active, project.Status);
        Assert.NotNull(project.DueDate);
    }
}
