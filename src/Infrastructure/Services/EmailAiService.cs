using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectOS.Application.Common;
using ProjectOS.Domain.Entities;
using ProjectOS.Infrastructure.Persistence;

namespace ProjectOS.Infrastructure.Services;

public class EmailAiService
{
    private readonly HttpClient _httpClient;
    private readonly AiOptions _options;
    private readonly AppDbContext _db;
    private readonly ILogger<EmailAiService> _logger;

    public EmailAiService(HttpClient httpClient, IOptions<AiOptions> options, AppDbContext db, ILogger<EmailAiService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _db = db;
        _logger = logger;
    }

    public async Task ProcessEmailAsync(EmailMessage email, CancellationToken ct = default)
    {
        _logger.LogInformation("EmailAiService called for subject: {Subject}", email.Subject);

        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? _options.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("OpenAI API key not configured — setting fallback values");
            email.AiSummary = "AI unavailable — no API key";
            email.AiSuggestedReply = "";
            email.AiCategory = "unknown";
            email.AiPriority = "medium";
            return;
        }

        // 1. Load tone profile
        var tone = await LoadToneProfileAsync(email.OrganizationId, ct);

        // 2. Detect language from email content
        var detectedLang = DetectLanguage(email.Subject, email.Body, tone);
        _logger.LogInformation("Language detected: {Lang} for email from {From}", detectedLang, email.FromAddress);

        // 3. Build prompt
        var bodyTruncated = email.Body.Length > 1500 ? email.Body[..1500] + "..." : email.Body;
        var toneBlock = BuildToneInstruction(tone);
        var systemPrompt = BuildSystemPrompt(toneBlock, detectedLang);

        _logger.LogInformation("AI prompt built — lang={Lang}, tone formality={Formality}, length={Length}",
            detectedLang, tone.Formality, tone.ResponseLength);

        var requestBody = new
        {
            model = _options.Model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
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
            email.AiCategory = Truncate(parsed?.Category ?? "unknown", 50);
            email.AiPriority = Truncate(parsed?.Priority ?? "medium", 20);

            // Reply-worthiness: suppress reply for non-actionable emails
            var noReplyCategories = new[] { "spam", "newsletter", "promotional", "system" };
            var cat = email.AiCategory.ToLowerInvariant();
            var needsReply = !noReplyCategories.Contains(cat) && !IsNoReplyEmail(email);

            if (needsReply)
            {
                email.AiSuggestedReply = Truncate(parsed?.Reply ?? "", 2000);
            }
            else
            {
                email.AiSuggestedReply = null;
                _logger.LogInformation("Reply suppressed for email {EmailId} — category={Cat}, no-reply={IsNoReply}",
                    email.Id, cat, !needsReply);
            }

            _logger.LogInformation("AI processed email {EmailId}: cat={Cat}, pri={Pri}, lang={Lang}, replyNeeded={Reply}",
                email.Id, email.AiCategory, email.AiPriority, detectedLang, needsReply);
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

    private static bool IsNoReplyEmail(EmailMessage email)
    {
        var from = (email.FromAddress ?? "").ToLowerInvariant();
        var subject = (email.Subject ?? "").ToLowerInvariant();
        var body = (email.Body ?? "").ToLowerInvariant();

        // No-reply sender addresses
        if (from.Contains("noreply") || from.Contains("no-reply") || from.Contains("donotreply") ||
            from.Contains("mailer-daemon") || from.Contains("postmaster@") || from.Contains("notifications@"))
            return true;

        // Newsletter/promotional signals in body
        var promoSignals = new[] { "unsubscribe", "click here to unsubscribe", "manage your preferences",
            "view in browser", "email preferences", "opt out", "you are receiving this" };
        var promoCount = 0;
        foreach (var s in promoSignals)
            if (body.Contains(s)) promoCount++;
        if (promoCount >= 2) return true;

        // Automated system emails
        var systemSubjects = new[] { "password reset", "verify your email", "login alert",
            "security alert", "order confirmation", "shipping notification", "delivery update",
            "payment receipt", "invoice #", "subscription", "your receipt" };
        foreach (var s in systemSubjects)
            if (subject.Contains(s)) return true;

        return false;
    }

    private async Task<UserToneProfile> LoadToneProfileAsync(Guid organizationId, CancellationToken ct)
    {
        try
        {
            var profile = await _db.UserToneProfiles
                .FirstOrDefaultAsync(p => p.OrganizationId == organizationId, ct);

            if (profile is not null)
            {
                _logger.LogInformation("Tone profile FOUND for org {OrgId}: formality={F}, length={L}, address={A}, traits={T}",
                    organizationId, profile.Formality, profile.ResponseLength, profile.AddressStyle, profile.PrimaryTraits);
                return profile;
            }

            _logger.LogInformation("No tone profile for org {OrgId} — using defaults", organizationId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load tone profile for org {OrgId} — using defaults", organizationId);
        }

        return new UserToneProfile();
    }

    private static string DetectLanguage(string subject, string body, UserToneProfile tone)
    {
        var sample = (subject + " " + body).ToLowerInvariant();

        // Check for clear English markers
        var englishWords = new[] { " the ", " is ", " are ", " was ", " were ", " have ", " has ", " will ", " would ", " please ", " thank you ", " regards ", " dear ", " hello ", " hi " };
        var englishScore = 0;
        foreach (var w in englishWords)
            if (sample.Contains(w)) englishScore++;

        // Check for clear Spanish markers
        var spanishWords = new[] { " el ", " la ", " los ", " las ", " es ", " son ", " tiene ", " hola ", " gracias ", " por favor ", " estimado ", " saludos ", " buenos ", " para ", " que ", " del " };
        var spanishScore = 0;
        foreach (var w in spanishWords)
            if (sample.Contains(w)) spanishScore++;

        // Clear winner
        if (englishScore >= 3 && englishScore > spanishScore * 2) return "English";
        if (spanishScore >= 2) return "Spanish";
        if (englishScore >= 2) return "English";

        // Check writing examples from tone profile
        var examples = ((tone.Example1 ?? "") + " " + (tone.Example2 ?? "")).ToLowerInvariant();
        if (examples.Length > 20)
        {
            var exEn = 0; var exEs = 0;
            foreach (var w in englishWords) if (examples.Contains(w)) exEn++;
            foreach (var w in spanishWords) if (examples.Contains(w)) exEs++;
            if (exEs > exEn) return "Spanish";
            if (exEn > exEs) return "English";
        }

        // Default to Spanish
        return "Spanish";
    }

    private static string BuildSystemPrompt(string toneBlock, string language)
    {
        return "You are an email assistant for business operations.\n\n" +
            "Respond with ONLY valid JSON (no code fences, no markdown):\n" +
            "{\"summary\": \"1-2 line summary\", \"reply\": \"suggested reply OR empty string if no reply needed\", \"category\": \"sales|support|spam|internal|billing|scheduling|newsletter|promotional|system\", \"priority\": \"high|medium|low\"}\n\n" +
            "═══ CRITICAL LANGUAGE RULE ═══\n" +
            $"The reply MUST be written in {language}.\n" +
            $"Do NOT use any language other than {language} for the reply field.\n" +
            "The summary may be in English for internal use.\n\n" +
            "═══ TONE PROFILE (MUST FOLLOW STRICTLY) ═══\n" +
            toneBlock + "\n" +
            "The reply MUST follow every aspect of this tone profile:\n" +
            "- Match the specified formality level exactly\n" +
            "- Respect the response length preference\n" +
            "- Use the address style specified (tu/usted/neutral)\n" +
            "- Embody the primary traits listed\n" +
            "- Avoid the traits listed under 'Avoid'\n" +
            "- If the sender seems upset, use the specified upset handling style\n" +
            "- If the email is sales-related, use the specified sales approach\n" +
            "- Include the signature if one is provided\n" +
            "- Match the writing style shown in the examples\n\n" +
            "═══ SAFETY RULES ═══\n" +
            "- Do not invent facts\n" +
            "- Do not take autonomous decisions — only suggest\n" +
            "- Do not hallucinate data not present in the email\n" +
            "- Be concise and actionable";
    }

    private static string BuildToneInstruction(UserToneProfile t)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"- Formality: {t.Formality}");
        sb.AppendLine($"- Response length: {t.ResponseLength}");
        sb.AppendLine($"- Address style: {t.AddressStyle}");
        sb.AppendLine($"- Primary traits: {t.PrimaryTraits}");
        sb.AppendLine($"- Avoid: {t.AvoidTraits}");
        sb.AppendLine($"- When sender is upset: be {t.UpsetStyle}");
        sb.AppendLine($"- Sales approach: {t.SalesStyle}");

        if (!string.IsNullOrWhiteSpace(t.Signature))
            sb.AppendLine($"- End reply with signature: {t.Signature}");

        if (!string.IsNullOrWhiteSpace(t.Example1))
            sb.AppendLine($"- Example of user's writing style: \"{t.Example1}\"");

        if (!string.IsNullOrWhiteSpace(t.Example2))
            sb.AppendLine($"- Another example: \"{t.Example2}\"");

        return sb.ToString();
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
