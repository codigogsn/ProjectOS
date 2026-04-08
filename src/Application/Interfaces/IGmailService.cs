namespace ProjectOS.Application.Interfaces;

public interface IGmailService
{
    Task<List<GmailMessageDto>> FetchRecentEmailsAsync(int maxResults = 50, CancellationToken ct = default);
}

public class GmailMessageDto
{
    public string MessageId { get; set; } = string.Empty;
    public string ThreadId { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string BodyText { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public List<string> To { get; set; } = new();
    public DateTime SentAtUtc { get; set; }
}
