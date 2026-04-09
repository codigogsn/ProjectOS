namespace ProjectOS.Application.Common;

public class GmailOptions
{
    public const string SectionName = "Gmail";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public string ApplicationName { get; set; } = "ProjectOS";
    public string UserEmail { get; set; } = "me";

    public string ResolveClientId() =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GMAIL_CLIENT_ID"))
            ? Environment.GetEnvironmentVariable("GMAIL_CLIENT_ID")!
            : ClientId;

    public string ResolveClientSecret() =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GMAIL_CLIENT_SECRET"))
            ? Environment.GetEnvironmentVariable("GMAIL_CLIENT_SECRET")!
            : ClientSecret;

    public string ResolveRefreshToken() =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GMAIL_REFRESH_TOKEN"))
            ? Environment.GetEnvironmentVariable("GMAIL_REFRESH_TOKEN")!
            : RefreshToken;
}
