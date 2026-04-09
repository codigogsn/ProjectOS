namespace ProjectOS.Application.Interfaces;

public interface IEmailIngestionService
{
    Task<EmailIngestionResult> IngestAsync(Guid organizationId, CancellationToken ct = default);
}

public class EmailIngestionResult
{
    public int Fetched { get; set; }
    public int Saved { get; set; }
    public int Duplicates { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
    public List<EmailFailure> Failures { get; set; } = new();
}

public class EmailFailure
{
    public string MessageId { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
