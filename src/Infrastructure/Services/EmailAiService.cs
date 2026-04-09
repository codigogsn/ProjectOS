using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectOS.Application.Common;
using ProjectOS.Domain.Entities;

namespace ProjectOS.Infrastructure.Services;

public class EmailAiService
{
    private readonly HttpClient _httpClient;
    private readonly AiOptions _options;
    private readonly ILogger<EmailAiService> _logger;

    public EmailAiService(HttpClient httpClient, IOptions<AiOptions> options, ILogger<EmailAiService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task ProcessEmailAsync(EmailMessage email, CancellationToken ct = default)
    {
        _logger.LogInformation("EmailAiService.ProcessEmailAsync called for subject: {Subject}", email.Subject);

        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? _options.ApiKey;

        _logger.LogInformation("OPENAI_API_KEY resolved: {HasKey} (length={Len})",
            !string.IsNullOrWhiteSpace(apiKey), apiKey?.Length ?? 0);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("OpenAI API key not configured — setting fallback values");
            email.AiSummary = "AI unavailable — no API key";
            email.AiSuggestedReply = "";
            email.AiCategory = "unknown";
            email.AiPriority = "medium";
            return;
        }

        var bodyTruncated = email.Body.Length > 1500 ? email.Body[..1500] + "..." : email.Body;

        var requestBody = new
        {
            model = _options.Model,
            messages = new[]
            {
                new { role = "system", content = "You are an email assistant for business operations. Analyze the email and respond with ONLY valid JSON (no code fences):\n{\"summary\": \"1-2 line summary\", \"reply\": \"short professional reply in same language as email\", \"category\": \"sales|support|spam|internal|billing|scheduling\", \"priority\": \"high|medium|low\"}\nRules: be concise, match the email language, do not invent facts." },
                new { role = "user", content = $"Subject: {email.Subject}\nFrom: {email.FromAddress}\n\n{bodyTruncated}" }
            },
            max_tokens = 512,
            temperature = 0.2
        };

        var json = JsonSerializer.Serialize(requestBody);
        var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl}/chat/completions")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var response = await _httpClient.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("AI email processing failed: {Status}", response.StatusCode);
            email.AiSummary = "AI processing failed";
            email.AiSuggestedReply = "";
            email.AiCategory = "unknown";
            email.AiPriority = "medium";
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "";

            content = content.Trim();
            if (content.StartsWith("```"))
            {
                var nl = content.IndexOf('\n');
                if (nl > 0) content = content[(nl + 1)..];
                if (content.EndsWith("```")) content = content[..^3];
                content = content.Trim();
            }

            var parsed = JsonSerializer.Deserialize<AiEmailResult>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            email.AiSummary = Truncate(parsed?.Summary ?? "No summary", 2000);
            email.AiSuggestedReply = Truncate(parsed?.Reply ?? "", 2000);
            email.AiCategory = Truncate(parsed?.Category ?? "unknown", 50);
            email.AiPriority = Truncate(parsed?.Priority ?? "medium", 20);

            _logger.LogInformation("AI processed email {EmailId}: category={Category}, priority={Priority}",
                email.Id, email.AiCategory, email.AiPriority);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse AI response for email {EmailId}", email.Id);
            email.AiSummary = "AI parse error";
            email.AiSuggestedReply = "";
            email.AiCategory = "unknown";
            email.AiPriority = "medium";
        }
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private class AiEmailResult
    {
        public string? Summary { get; set; }
        public string? Reply { get; set; }
        public string? Category { get; set; }
        public string? Priority { get; set; }
    }
}
