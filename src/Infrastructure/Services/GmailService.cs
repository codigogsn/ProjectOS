using System.Text;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectOS.Application.Common;
using ProjectOS.Application.Interfaces;

namespace ProjectOS.Infrastructure.Services;

public class GmailService : IGmailService, IDisposable
{
    private readonly GmailOptions _options;
    private readonly ILogger<GmailService> _logger;
    private Google.Apis.Gmail.v1.GmailService? _gmailClient;

    public GmailService(IOptions<GmailOptions> options, ILogger<GmailService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<List<GmailMessageDto>> FetchRecentEmailsAsync(int maxResults = 50, CancellationToken ct = default)
    {
        var service = GetGmailClient();
        var results = new List<GmailMessageDto>();

        var listRequest = service.Users.Messages.List(_options.UserEmail);
        listRequest.MaxResults = maxResults;
        listRequest.LabelIds = "INBOX";

        var listResponse = await listRequest.ExecuteAsync(ct);

        if (listResponse.Messages == null || listResponse.Messages.Count == 0)
        {
            _logger.LogInformation("No messages found in Gmail inbox");
            return results;
        }

        _logger.LogInformation("Found {Count} message IDs in Gmail", listResponse.Messages.Count);

        foreach (var msgRef in listResponse.Messages)
        {
            try
            {
                var getRequest = service.Users.Messages.Get(_options.UserEmail, msgRef.Id);
                getRequest.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Full;

                var message = await getRequest.ExecuteAsync(ct);
                var dto = MapToDto(message);
                if (dto is not null)
                    results.Add(dto);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch Gmail message {MessageId}", msgRef.Id);
            }
        }

        _logger.LogInformation("Successfully fetched {Count} emails from Gmail", results.Count);
        return results;
    }

    private GmailMessageDto? MapToDto(Message message)
    {
        var headers = message.Payload?.Headers;
        if (headers == null) return null;

        var subject = GetHeader(headers, "Subject") ?? "(no subject)";
        var from = GetHeader(headers, "From") ?? "";
        var to = GetHeader(headers, "To") ?? "";
        var dateStr = GetHeader(headers, "Date");

        var sentAt = DateTime.UtcNow;
        if (message.InternalDate.HasValue)
        {
            sentAt = DateTimeOffset.FromUnixTimeMilliseconds(message.InternalDate.Value).UtcDateTime;
        }
        else if (dateStr is not null && DateTimeOffset.TryParse(dateStr, out var parsed))
        {
            sentAt = parsed.UtcDateTime;
        }

        var bodyText = ExtractBodyText(message.Payload);

        var toAddresses = to
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ExtractEmailAddress)
            .Where(e => !string.IsNullOrEmpty(e))
            .ToList();

        return new GmailMessageDto
        {
            MessageId = message.Id,
            ThreadId = message.ThreadId,
            Subject = subject,
            BodyText = bodyText,
            From = ExtractEmailAddress(from),
            To = toAddresses,
            SentAtUtc = sentAt
        };
    }

    private static string ExtractBodyText(MessagePart? part)
    {
        if (part == null) return string.Empty;

        if (part.MimeType == "text/plain" && part.Body?.Data is not null)
        {
            return DecodeBase64Url(part.Body.Data);
        }

        if (part.Parts != null)
        {
            foreach (var child in part.Parts)
            {
                var text = ExtractBodyText(child);
                if (!string.IsNullOrEmpty(text))
                    return text;
            }
        }

        if (part.Body?.Data is not null)
        {
            return DecodeBase64Url(part.Body.Data);
        }

        return string.Empty;
    }

    private static string DecodeBase64Url(string base64Url)
    {
        var base64 = base64Url.Replace('-', '+').Replace('_', '/');
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }
        var bytes = Convert.FromBase64String(base64);
        return Encoding.UTF8.GetString(bytes);
    }

    private static string ExtractEmailAddress(string headerValue)
    {
        if (string.IsNullOrWhiteSpace(headerValue))
            return string.Empty;

        var openAngle = headerValue.LastIndexOf('<');
        var closeAngle = headerValue.LastIndexOf('>');
        if (openAngle >= 0 && closeAngle > openAngle)
        {
            return headerValue[(openAngle + 1)..closeAngle].Trim().ToLowerInvariant();
        }

        return headerValue.Trim().ToLowerInvariant();
    }

    private static string? GetHeader(IList<MessagePartHeader> headers, string name)
    {
        return headers.FirstOrDefault(h =>
            string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;
    }

    private Google.Apis.Gmail.v1.GmailService GetGmailClient()
    {
        if (_gmailClient is not null) return _gmailClient;

        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets
            {
                ClientId = _options.ClientId,
                ClientSecret = _options.ClientSecret
            },
            Scopes = new[] { Google.Apis.Gmail.v1.GmailService.Scope.GmailReadonly }
        });

        var credential = new UserCredential(flow, _options.UserEmail, new TokenResponse
        {
            RefreshToken = _options.RefreshToken
        });

        _gmailClient = new Google.Apis.Gmail.v1.GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = _options.ApplicationName
        });

        return _gmailClient;
    }

    public void Dispose()
    {
        _gmailClient?.Dispose();
    }
}
