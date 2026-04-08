namespace ProjectOS.Application.Common;

public class GmailOptions
{
    public const string SectionName = "Gmail";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public string ApplicationName { get; set; } = "ProjectOS";
    public string UserEmail { get; set; } = "me";
}
