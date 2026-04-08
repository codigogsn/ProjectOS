namespace ProjectOS.Domain.Entities;

public class Organization : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Website { get; set; }
    public string? LogoUrl { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<Project> Projects { get; set; } = new List<Project>();
    public ICollection<Contact> Contacts { get; set; } = new List<Contact>();
    public ICollection<User> Users { get; set; } = new List<User>();
}
