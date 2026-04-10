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
            email.AiSummary = "AI unavailable — no API key";
            email.AiSuggestedReply = "";
            email.AiCategory = "unknown";
            email.AiPriority = "medium";
            email.AiReplyIntent = "unknown";
            return;
        }

        var tone = await LoadToneProfileAsync(email.OrganizationId, ct);
        var detectedLang = DetectLanguage(email.Subject, email.Body, tone);
        var isForward = DetectForward(email.Subject, email.Body);

        _logger.LogInformation("Processing email: lang={Lang}, forward={Fwd}, from={From}",
            detectedLang, isForward, email.FromAddress);

        var bodyTruncated = email.Body.Length > 1500 ? email.Body[..1500] + "..." : email.Body;
        var toneBlock = BuildToneInstruction(tone);
        var systemPrompt = BuildSystemPrompt(toneBlock, detectedLang, isForward);

        var requestBody = new
        {
            model = _options.Model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = $"Subject: {email.Subject}\nFrom: {email.FromAddress}\n\n{bodyTruncated}" }
            },
            max_tokens = 900,
            temperature = 0.35
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
            email.AiReplyIntent = "unknown";
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
            email.AiReplyIntent = Truncate(parsed?.ReplyIntent ?? "direct", 50);

            // Reply-worthiness
            var noReplyCategories = new[] { "spam", "newsletter", "promotional", "system" };
            var cat = email.AiCategory.ToLowerInvariant();
            var intent = (email.AiReplyIntent ?? "").ToLowerInvariant();
            var needsReply = !noReplyCategories.Contains(cat) && !IsNoReplyEmail(email) && intent != "no_reply";

            if (needsReply)
            {
                // Store balanced as main reply
                email.AiSuggestedReply = Truncate(parsed?.ReplyBalanced ?? parsed?.Reply ?? "", 2000);

                // Store all 3 variants as JSON
                var variants = new
                {
                    concise = parsed?.ReplyConcise ?? "",
                    balanced = parsed?.ReplyBalanced ?? parsed?.Reply ?? "",
                    warmer = parsed?.ReplyWarmer ?? ""
                };
                email.AiReplyVariants = JsonSerializer.Serialize(variants);
            }
            else
            {
                email.AiSuggestedReply = null;
                email.AiReplyVariants = null;
                _logger.LogInformation("Reply suppressed: cat={Cat}, intent={Intent}", cat, intent);
            }

            _logger.LogInformation("AI done {EmailId}: cat={Cat}, pri={Pri}, intent={Intent}, hasVariants={V}",
                email.Id, email.AiCategory, email.AiPriority, email.AiReplyIntent, email.AiReplyVariants != null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse AI response for email {EmailId}", email.Id);
            email.AiSummary = "AI parse error";
            email.AiSuggestedReply = "";
            email.AiCategory = "unknown";
            email.AiPriority = "medium";
            email.AiReplyIntent = "unknown";
        }
    }

    private static bool DetectForward(string subject, string body)
    {
        var subLower = (subject ?? "").ToLowerInvariant();
        if (subLower.StartsWith("fwd:") || subLower.StartsWith("fw:") || subLower.StartsWith("reenviad"))
            return true;
        var bodyLower = (body ?? "").ToLowerInvariant();
        return bodyLower.Contains("---------- forwarded message") || bodyLower.Contains("--- forwarded") || bodyLower.Contains("--- mensaje reenviado");
    }

    private static bool IsNoReplyEmail(EmailMessage email)
    {
        var from = (email.FromAddress ?? "").ToLowerInvariant();
        var subject = (email.Subject ?? "").ToLowerInvariant();
        var body = (email.Body ?? "").ToLowerInvariant();

        if (from.Contains("noreply") || from.Contains("no-reply") || from.Contains("donotreply") ||
            from.Contains("mailer-daemon") || from.Contains("postmaster@") || from.Contains("notifications@"))
            return true;

        var promoSignals = new[] { "unsubscribe", "click here to unsubscribe", "manage your preferences",
            "view in browser", "email preferences", "opt out", "you are receiving this" };
        var promoCount = 0;
        foreach (var s in promoSignals)
            if (body.Contains(s)) promoCount++;
        if (promoCount >= 2) return true;

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
                _logger.LogInformation("Tone profile loaded: formality={F}, traits={T}", profile.Formality, profile.PrimaryTraits);
                return profile;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load tone profile — using defaults");
        }
        return new UserToneProfile();
    }

    private static string DetectLanguage(string subject, string body, UserToneProfile tone)
    {
        var sample = (subject + " " + body).ToLowerInvariant();
        var englishWords = new[] { " the ", " is ", " are ", " was ", " were ", " have ", " has ", " will ", " would ", " please ", " thank you ", " regards ", " dear ", " hello ", " hi " };
        var spanishWords = new[] { " el ", " la ", " los ", " las ", " es ", " son ", " tiene ", " hola ", " gracias ", " por favor ", " estimado ", " saludos ", " buenos ", " para ", " que ", " del " };
        var en = 0; var es = 0;
        foreach (var w in englishWords) if (sample.Contains(w)) en++;
        foreach (var w in spanishWords) if (sample.Contains(w)) es++;
        if (en >= 3 && en > es * 2) return "English";
        if (es >= 2) return "Spanish";
        if (en >= 2) return "English";
        var examples = ((tone.Example1 ?? "") + " " + (tone.Example2 ?? "")).ToLowerInvariant();
        if (examples.Length > 20)
        {
            var exEn = 0; var exEs = 0;
            foreach (var w in englishWords) if (examples.Contains(w)) exEn++;
            foreach (var w in spanishWords) if (examples.Contains(w)) exEs++;
            if (exEs > exEn) return "Spanish";
            if (exEn > exEs) return "English";
        }
        return "Spanish";
    }

    private static string BuildSystemPrompt(string toneBlock, string language, bool isForward)
    {
        var forwardContext = isForward
            ? "\n═══ FORWARD CONTEXT ═══\n" +
              "This email is FORWARDED content. The sender is sharing something, not writing to you directly.\n" +
              "- Determine if a reply to the SENDER (the forwarder) is needed\n" +
              "- Do NOT reply to the original author of the forwarded content\n" +
              "- If it's shared for awareness only: set reply_intent to 'optional'\n" +
              "- If it clearly asks for input: reply to the forwarder about the forwarded topic\n\n"
            : "";

        return "You are drafting replies on behalf of a real business operator. Write like the actual operator, not like an AI, customer support macro, or generic template.\n\n" +
            "Respond with ONLY valid JSON (no code fences):\n" +
            "{\n" +
            "  \"summary\": \"1-2 line summary\",\n" +
            "  \"reply_concise\": \"shortest useful reply (1-2 sentences)\",\n" +
            "  \"reply_balanced\": \"standard reply (3-5 sentences)\",\n" +
            "  \"reply_warmer\": \"warmer, more personal variant\",\n" +
            "  \"reply_intent\": \"direct|optional|no_reply\",\n" +
            "  \"category\": \"sales|support|spam|internal|billing|scheduling|newsletter|promotional|system\",\n" +
            "  \"priority\": \"high|medium|low\"\n" +
            "}\n\n" +

            forwardContext +

            "═══ ABSOLUTE LANGUAGE RULE ═══\n" +
            $"ALL three reply variants MUST be written entirely in {language}. Every word. No exceptions.\n" +
            $"Do NOT use any word from a language other than {language}.\n\n" +

            "═══ TONE PROFILE (MANDATORY) ═══\n" +
            toneBlock + "\n" +
            "HARD RULES:\n" +
            "- All 3 variants must follow this tone profile\n" +
            "- reply_concise: MAX 1-2 sentences. Ultra direct.\n" +
            "- reply_balanced: 3-5 sentences. Clear and complete.\n" +
            "- reply_warmer: Same content as balanced but with more warmth, empathy, personal touch.\n" +
            "- Each variant must have a DIFFERENT opening line\n" +
            "- If writing examples exist: mimic their phrasing, rhythm, and word choices\n" +
            "- Include signature at the end of each variant if provided\n\n" +

            "═══ HUMAN QUALITY (CRITICAL) ═══\n" +
            "- BANNED phrases: 'I hope this email finds you well', 'Please don't hesitate', 'I'd be happy to assist', 'As per your request', 'Thank you for reaching out', 'I wanted to follow up'\n" +
            "- NEVER start with 'I hope' or 'Thank you for your email'\n" +
            "- Get to the point immediately\n" +
            "- Reference specific details from the email\n" +
            "- Sound natural, not corporate-templated\n" +
            "- Do not repeat the summary in the reply\n" +
            "- Do not over-explain\n\n" +

            "═══ SAFETY ═══\n" +
            "- Do not invent facts\n" +
            "- Do not take autonomous decisions — only suggest\n" +
            "- Do not hallucinate data";
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
            sb.AppendLine($"- Signature: {t.Signature}");
        if (!string.IsNullOrWhiteSpace(t.Example1))
            sb.AppendLine($"- Writing example 1: \"{t.Example1}\"");
        if (!string.IsNullOrWhiteSpace(t.Example2))
            sb.AppendLine($"- Writing example 2: \"{t.Example2}\"");
        return sb.ToString();
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private class AiEmailResult
    {
        public string? Summary { get; set; }
        public string? Reply { get; set; }
        public string? ReplyConcise { get; set; }
        public string? ReplyBalanced { get; set; }
        public string? ReplyWarmer { get; set; }
        public string? ReplyIntent { get; set; }
        public string? Category { get; set; }
        public string? Priority { get; set; }
    }
}
