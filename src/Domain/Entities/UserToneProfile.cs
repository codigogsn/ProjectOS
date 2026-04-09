namespace ProjectOS.Domain.Entities;

public class UserToneProfile : BaseEntity
{
    public Guid OrganizationId { get; set; }
    public string Formality { get; set; } = "professional";
    public string ResponseLength { get; set; } = "medium";
    public string AddressStyle { get; set; } = "neutral";
    public string PrimaryTraits { get; set; } = "clear";
    public string AvoidTraits { get; set; } = "robotic";
    public string UpsetStyle { get; set; } = "empathetic";
    public string SalesStyle { get; set; } = "consultative";
    public string? Signature { get; set; }
    public string? Example1 { get; set; }
    public string? Example2 { get; set; }
}
