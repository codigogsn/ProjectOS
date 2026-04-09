using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectOS.Application.Common;
using ProjectOS.Application.Interfaces;
using ProjectOS.Domain.Entities;
using ProjectOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ProjectOS.Infrastructure.Services;

public class ProjectSummaryService : IProjectSummaryService
{
    private readonly AppDbContext _db;
    private readonly IEmailMessageRepository _emailRepo;
    private readonly IProjectRepository _projectRepo;
    private readonly HttpClient _httpClient;
    private readonly AiOptions _aiOptions;
    private readonly ILogger<ProjectSummaryService> _logger;

    public ProjectSummaryService(
        AppDbContext db,
        IEmailMessageRepository emailRepo,
        IProjectRepository projectRepo,
        HttpClient httpClient,
        IOptions<AiOptions> aiOptions,
        ILogger<ProjectSummaryService> logger)
    {
        _db = db;
        _emailRepo = emailRepo;
        _projectRepo = projectRepo;
        _httpClient = httpClient;
        _aiOptions = aiOptions.Value;
        _logger = logger;
    }

    public async Task<ProjectSummary> GenerateSummaryAsync(Guid projectId, CancellationToken ct = default)
    {
        var project = await _projectRepo.GetByIdAsync(projectId, ct)
            ?? throw new KeyNotFoundException($"Project {projectId} not found");

        var emails = await _emailRepo.GetRecentByProjectIdAsync(projectId, 25, ct);

        _logger.LogInformation("Generating summary for project {ProjectId} with {EmailCount} emails",
            projectId, emails.Count);

        var emailContext = FormatEmailContext(emails);
        var aiResponse = await CallAiAsync(project.Name, emailContext, ct);

        var summary = new ProjectSummary
        {
            ProjectId = projectId,
            SummaryText = aiResponse.Summary,
            CurrentStatus = aiResponse.CurrentStatus,
            PendingItems = aiResponse.PendingItems,
            SuggestedNextAction = aiResponse.SuggestedNextAction,
            GeneratedAtUtc = DateTime.UtcNow
        };

        _db.ProjectSummaries.Add(summary);

        // Extract and persist action items from PendingItems
        await ExtractActionItemsAsync(projectId, aiResponse.PendingItems, aiResponse.SuggestedNextAction, ct);

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Summary {SummaryId} generated for project {ProjectId}", summary.Id, projectId);
        return summary;
    }

    public async Task<ProjectSummary?> GetLatestSummaryAsync(Guid projectId, CancellationToken ct = default)
    {
        return await _db.ProjectSummaries
            .Where(s => s.ProjectId == projectId)
            .OrderByDescending(s => s.GeneratedAtUtc)
            .FirstOrDefaultAsync(ct);
    }

    private async Task ExtractActionItemsAsync(Guid projectId, string pendingItems, string suggestedNextAction, CancellationToken ct)
    {
        // Remove previous pending action items for this project
        var existing = await _db.ActionItems
            .Where(a => a.ProjectId == projectId && a.Status == "Pending")
            .ToListAsync(ct);
        _db.ActionItems.RemoveRange(existing);

        var items = new List<string>();

        // Parse bullet list from PendingItems
        if (!string.IsNullOrWhiteSpace(pendingItems))
        {
            var lines = pendingItems.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var cleaned = line.TrimStart('-', '*', ' ', '\t').Trim();
                if (!string.IsNullOrWhiteSpace(cleaned))
                    items.Add(cleaned);
            }
        }

        // Add suggested next action as an action item
        if (!string.IsNullOrWhiteSpace(suggestedNextAction))
        {
            var cleaned = suggestedNextAction.Trim();
            if (!items.Contains(cleaned, StringComparer.OrdinalIgnoreCase))
                items.Add(cleaned);
        }

        var priority = 0;
        foreach (var item in items)
        {
            _db.ActionItems.Add(new ActionItem
            {
                Title = item.Length > 500 ? item[..500] : item,
                Status = "Pending",
                Priority = priority++,
                ProjectId = projectId
            });
        }

        _logger.LogInformation("Extracted {Count} action items for project {ProjectId}", items.Count, projectId);
    }

    private static string FormatEmailContext(List<EmailMessage> emails)
    {
        var sb = new StringBuilder();
        foreach (var e in emails.OrderBy(x => x.SentAtUtc))
        {
            sb.AppendLine($"---");
            sb.AppendLine($"Date: {e.SentAtUtc:yyyy-MM-dd HH:mm}");
            sb.AppendLine($"From: {e.FromAddress}");
            sb.AppendLine($"To: {e.ToAddress}");
            sb.AppendLine($"Subject: {e.Subject}");
            var body = e.Body.Length > 800 ? e.Body[..800] + "..." : e.Body;
            sb.AppendLine($"Body: {body}");
        }
        return sb.ToString();
    }

    private async Task<AiSummaryResponse> CallAiAsync(string projectName, string emailContext, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_aiOptions.ApiKey))
        {
            _logger.LogWarning("AI API key not configured. Returning placeholder summary.");
            return new AiSummaryResponse
            {
                Summary = "AI summary unavailable - API key not configured. Configure the Ai:ApiKey setting to enable AI summaries.",
                CurrentStatus = "Unknown",
                PendingItems = "- Configure AI API key",
                SuggestedNextAction = "Set up the AI provider API key in configuration."
            };
        }

        var systemPrompt = @"You are a project analyst. Given a project name and its email thread, produce a structured summary.
Respond ONLY with valid JSON in this exact format:
{
  ""summary"": ""2-4 sentence overview of what has happened in this project"",
  ""currentStatus"": ""One sentence describing current state"",
  ""pendingItems"": ""Bullet list of pending/open items, each on a new line starting with -"",
  ""suggestedNextAction"": ""One concrete next action to move this project forward""
}

Rules:
- Only use information from the provided emails. Do not invent facts.
- Be concise and actionable.
- If there is not enough information, say so honestly.";

        var userPrompt = $"Project: {projectName}\n\nEmails:\n{emailContext}";

        var requestBody = new
        {
            model = _aiOptions.Model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            max_tokens = _aiOptions.MaxTokens,
            temperature = 0.3
        };

        var json = JsonSerializer.Serialize(requestBody);
        var request = new HttpRequestMessage(HttpMethod.Post, $"{_aiOptions.BaseUrl}/chat/completions")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _aiOptions.ApiKey);

        var response = await _httpClient.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("AI API returned {StatusCode}: {Body}", response.StatusCode, responseBody);
            return new AiSummaryResponse
            {
                Summary = $"AI summary generation failed (HTTP {(int)response.StatusCode}).",
                CurrentStatus = "Error",
                PendingItems = "- Retry summary generation",
                SuggestedNextAction = "Check AI provider configuration and retry."
            };
        }

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "";

            // Strip markdown code fences if present
            content = content.Trim();
            if (content.StartsWith("```"))
            {
                var firstNewline = content.IndexOf('\n');
                if (firstNewline > 0) content = content[(firstNewline + 1)..];
                if (content.EndsWith("```")) content = content[..^3];
                content = content.Trim();
            }

            var parsed = JsonSerializer.Deserialize<AiSummaryResponse>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return parsed ?? new AiSummaryResponse
            {
                Summary = content,
                CurrentStatus = "See summary",
                PendingItems = "",
                SuggestedNextAction = ""
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse AI response as structured JSON");
            return new AiSummaryResponse
            {
                Summary = "AI returned a response but it could not be parsed. Raw response logged.",
                CurrentStatus = "Parse error",
                PendingItems = "- Retry summary generation",
                SuggestedNextAction = "Retry or check AI model output format."
            };
        }
    }

    private class AiSummaryResponse
    {
        public string Summary { get; set; } = string.Empty;
        public string CurrentStatus { get; set; } = string.Empty;
        public string PendingItems { get; set; } = string.Empty;
        public string SuggestedNextAction { get; set; } = string.Empty;
    }
}
