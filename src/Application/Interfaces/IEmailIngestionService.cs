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
}
